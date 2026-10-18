import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { browser, By, visible, click, button, heading, open, timeout } from './lib/browser.mjs';

const publicOnly = process.env.E2E_PUBLIC_ONLY === 'true';
async function scenario(t, action) {
  let driver;
  try { driver = await browser(); await action(driver); }
  catch (error) {
    if (driver) {
      await mkdir('reports/screenshots', { recursive: true });
      // Do not capture login fields, tokens, page source or browser storage.
      if (!new URL(await driver.getCurrentUrl()).pathname.startsWith('/login')) {
        const path = `reports/screenshots/${t.name.replace(/[^a-z0-9]+/gi, '-')}.png`;
        await writeFile(path, await driver.takeScreenshot(), 'base64').catch(() => {});
      }
    }
    throw error;
  } finally { if (driver) await driver.quit(); }
}
async function login(driver) {
  assert.ok(process.env.E2E_EMAIL && process.env.E2E_PASSWORD,
    'Set E2E_EMAIL and E2E_PASSWORD to a dedicated Customer test account. Use npm run test:public for public checks only.');
  await open(driver, '/login');
  await (await visible(driver, By.css('input[type=email]'))).sendKeys(process.env.E2E_EMAIL);
  await (await visible(driver, By.css('input[type=password]'))).sendKeys(process.env.E2E_PASSWORD);
  await click(driver, button('Sign In'));
  await driver.wait(async () => new URL(await driver.getCurrentUrl()).pathname !== '/login', timeout, 'Customer login failed');
  await open(driver, '/cart');
  await visible(driver, heading('Your Shopping Cart'));
}
const cards = By.css('.products-grid .product-card');
const rows = By.css('.cart-item-row');
async function cartReady(driver) {
  await driver.wait(async () => !(await driver.findElements(By.xpath("//*[text()='Loading your basket…']"))).length, timeout);
  assert.equal((await driver.findElements(By.css('[role=alert]'))).length, 0, 'Basket API reported an error');
}
async function quantity(driver, expected) {
  await driver.wait(async () => {
    const elements = await driver.findElements(By.css('.cart-item-row .cart-item-actions > div:first-child > span'));
    return elements.length === 1 && Number(await elements[0].getText()) === expected;
  }, timeout, `Expected basket quantity ${expected}`);
}
async function money(driver, label) {
  const el = await visible(driver, By.xpath(`//span[normalize-space(.)='${label}']/following-sibling::*[1]`));
  return Number((await el.getText()).replace(/[^0-9.-]/g, ''));
}

test('E2E-01 real catalogue loads and search filters products', async t => scenario(t, async d => {
  await open(d, '/categories');
  await visible(d, heading('Shop Fresh Catalog'));
  const first = await visible(d, By.css('.product-card .product-name'));
  const name = await first.getText();
  assert.ok(name.trim());
  const search = await visible(d, By.css('input[placeholder="Product name or SKU..."]'));
  await search.sendKeys(name);
  await d.wait(async () => {
    const names = await d.findElements(By.css('.product-card .product-name'));
    return names.length > 0 && (await Promise.all(names.map(x => x.getText()))).every(x => x.toLowerCase().includes(name.toLowerCase()));
  }, timeout);
  await search.clear();
  await search.sendKeys('E2E-NO-MATCH-6d0b9baf');
  await visible(d, heading('No items match your filter selection.'));
  assert.equal((await d.findElements(cards)).length, 0);
  await click(d, button('Clear All Filters'));
  await visible(d, By.css('.product-card'));
}));

test('E2E-02 catalogue price sorting is ascending', async t => scenario(t, async d => {
  await open(d, '/categories');
  await visible(d, By.css('.product-card'));
  await click(d, By.css('select option[value=PRICE_LOW]'));
  await d.wait(async () => {
    const prices = await d.findElements(By.css('.product-card .price-tag'));
    const values = await Promise.all(prices.map(async el => Number((await el.getText()).replace(/[^0-9.-]/g, ''))));
    return values.length >= 2 && values.every((v, i) => Number.isFinite(v) && (!i || values[i - 1] <= v));
  }, timeout, 'Need at least two products and ascending prices');
}));

test('E2E-03 guest cannot access cart or checkout', async t => scenario(t, async d => {
  for (const path of ['/cart', '/checkout']) {
    await open(d, path);
    await visible(d, heading('Welcome Back'));
    assert.equal(new URL(await d.getCurrentUrl()).pathname, '/login');
    assert.equal((await d.findElements(heading('Express Checkout'))).length, 0);
  }
}));

test('E2E-04 invalid credentials are rejected by identity service', async t => scenario(t, async d => {
  await open(d, '/login');
  await (await visible(d, By.css('input[type=email]'))).sendKeys('e2e-nonexistent-6d0b9baf@example.invalid');
  await (await visible(d, By.css('input[type=password]'))).sendKeys('invalid-test-password');
  await click(d, button('Sign In'));
  // Match the application's authentication error; a network error must not count as success.
  await visible(d, By.xpath("//div[contains(@style,'background') and (contains(.,'Invalid credentials') or contains(.,'Invalid email or password') or normalize-space(.)='Request failed with status code 401' or normalize-space(.)='Unauthorized') and not(.//div)]"));
  assert.equal(new URL(await d.getCurrentUrl()).pathname, '/login');
  assert.equal(await d.executeScript('return !!localStorage.getItem("marketflowToken")'), false);
}));

test('E2E-05 customer login cart persistence quantity totals checkout and cleanup', { skip: publicOnly }, async t => scenario(t, async d => {
  await login(d);
  await cartReady(d);
  // Never erase an existing customer's basket. A dedicated empty account is required.
  assert.equal((await d.findElements(rows)).length, 0, 'Test account basket must be empty; no existing items were modified');
  await visible(d, heading('Your Cart is Empty'));
  let added = false;
  let productName;
  try {
    await open(d, '/categories');
    const product = await visible(d, By.xpath("//div[contains(concat(' ',normalize-space(@class),' '),' product-card ')][.//div[contains(.,'items available') and number(substring-before(normalize-space(.),' items available')) >= 2]]"));
    const name = await product.findElement(By.css('.product-name')).getText();
    productName = name;
    const price = Number((await product.findElement(By.css('.price-tag')).getText()).replace(/[^0-9.-]/g, ''));
    added = true; // cleanup also covers partial/late API success
    await product.findElement(By.css('.product-price-row button')).click();
    // Wait for the basket count in the navbar, avoiding navigation while PUT is in flight.
    await d.wait(async () => (await d.findElements(By.css('a[href="/cart"] .badge-count'))).length > 0, timeout);
    await click(d, By.css('a[href="/cart"]'));
    await visible(d, By.css('.cart-item-row'));
    await quantity(d, 1);
    assert.equal(await (await d.findElement(By.css('.cart-item-row h3'))).getText(), name);
    await click(d, By.css('.cart-item-actions button[title="Increase quantity"]'));
    await quantity(d, 2);
    assert.equal(await money(d, 'Subtotal'), Number((price * 2).toFixed(2)));
    assert.equal(await money(d, 'Total'), Number(((await money(d, 'Subtotal')) + (await money(d, 'Express Shipping'))).toFixed(2)));
    await d.navigate().refresh();
    await visible(d, heading('Your Shopping Cart'));
    await quantity(d, 2); // proves the order-service basket survived a page reload
    await click(d, button('Proceed to Checkout'));
    await visible(d, heading('Express Checkout'));
    await visible(d, heading('Delivery Address'));
    await visible(d, heading('Payment Method'));
    await click(d, By.xpath("//button[.//strong[normalize-space(.)='Bank Transfer']]"));
    const place = await visible(d, button('Place Order'));
    assert.equal(await place.isEnabled(), false, 'Bank transfer must require a receipt');
    // Optional, explicit opt-in only: creates ONE real COD order with an existing test address.
    if (process.env.E2E_ALLOW_ORDER === 'true') {
      await click(d, By.xpath("//button[.//strong[normalize-space(.)='Cash on Delivery']]"));
      assert.ok((await d.findElements(By.css('input[name=address]'))).length, 'Prepare a saved test delivery address first');
      await click(d, button('Place Order'));
      await visible(d, heading('Order Received!'));
      const orderId = (await (await d.findElement(By.xpath("//h1[normalize-space(.)='Order Received!']/following-sibling::p/strong"))).getText()).replace(/^#/, '');
      assert.ok(orderId);
      t.diagnostic(`Created test order ${orderId}; reconcile with team after demo.`);
      await click(d, button('View My Orders'));
      await d.wait(async () => (await d.findElement(By.css('body')).getText()).includes(orderId), timeout, 'Created order missing from history');
    }
  } finally {
    if (added) {
      await open(d, '/cart');
      await visible(d, heading('Your Shopping Cart'));
      await cartReady(d);
      const lines = await d.findElements(rows);
      assert.ok(lines.length <= 1, 'Unexpected concurrent basket changes; refusing broad cleanup');
      if (lines.length) {
        assert.equal(await lines[0].findElement(By.css('h3')).getText(), productName, 'Basket changed concurrently; refusing to remove another product');
        await click(d, By.css('.cart-item-actions > button'));
        await visible(d, heading('Your Cart is Empty'));
      }
    }
  }
}));
