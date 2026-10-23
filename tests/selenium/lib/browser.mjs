import { Builder, By, until } from 'selenium-webdriver';
import chrome from 'selenium-webdriver/chrome.js';
export { By };
export const timeout = Number(process.env.E2E_TIMEOUT_MS || 20000);
export const appUrl = process.env.APP_URL || 'https://gray-rock-0d7c57400.2.azurestaticapps.net';
export async function browser() {
  const options = new chrome.Options().addArguments('--window-size=1440,1000', '--disable-dev-shm-usage');
  if (process.env.HEADLESS !== 'false') options.addArguments('--headless=new');
  if (process.env.CHROME_NO_SANDBOX === 'true') options.addArguments('--no-sandbox');
  if (process.env.CHROME_BINARY) options.setChromeBinaryPath(process.env.CHROME_BINARY);
  let builder = new Builder().forBrowser('chrome').setChromeOptions(options).setChromeService(new chrome.ServiceBuilder().setHostname('127.0.0.1'));
  if (process.env.SELENIUM_REMOTE_URL) builder = builder.usingServer(process.env.SELENIUM_REMOTE_URL);
  const driver = await builder.build();
  await driver.manage().setTimeouts({ implicit: 0, pageLoad: 45000, script: 20000 });
  return driver;
}
export async function visible(driver, locator) {
  const element = await driver.wait(until.elementLocated(locator), timeout);
  await driver.wait(until.elementIsVisible(element), timeout);
  return element;
}
export async function click(driver, locator) {
  const element = await visible(driver, locator);
  await driver.wait(until.elementIsEnabled(element), timeout);
  await driver.executeScript('arguments[0].scrollIntoView({block:"center"})', element);
  await element.click();
}
export const button = (text) => By.xpath(`//button[normalize-space(.)='${text}']`);
export const heading = (text) => By.xpath(`//*[self::h1 or self::h2 or self::h3][normalize-space(.)='${text}']`);
export async function open(driver, path) { await driver.get(new URL(path, appUrl).href); }
