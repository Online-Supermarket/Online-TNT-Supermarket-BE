import { Builder, By, until } from 'selenium-webdriver';

const appUrl = process.env.APP_URL || 'http://localhost:5173';
const remoteUrl = process.env.SELENIUM_REMOTE_URL || 'http://localhost:4444/wd/hub';
const driver = await new Builder().usingServer(remoteUrl).forBrowser('chrome').build();
try {
  await driver.get(appUrl);
  await driver.wait(until.elementLocated(By.css('input[aria-label="Search products"]')), 15000);
  await driver.findElement(By.css('button.primary')).click();
  await driver.wait(until.elementLocated(By.css('input[type="email"]')), 5000);
  await driver.findElement(By.css('input[type="email"]')).sendKeys('customer@marketflow.local');
  await driver.findElement(By.css('input[type="password"]')).sendKeys('ChangeMe!123');
  await driver.findElement(By.css('form button.primary')).click();
  await driver.wait(until.elementLocated(By.xpath("//*[contains(., 'Your basket')]")), 10000);
  console.log('Sprint 1-2 Selenium sign-in and basket scenario passed.');
} finally {
  await driver.quit();
}
