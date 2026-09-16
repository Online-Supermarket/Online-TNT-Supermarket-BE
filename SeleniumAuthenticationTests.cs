using System;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

class SeleniumAuthenticationTests
{
    static void Main()
    {
        Console.WriteLine("=== TNT Supermarket - Selenium Automated UI Testing ===");
        
        var options = new ChromeOptions();
        // options.AddArgument("--headless"); // Uncomment to run without opening GUI window
        using IWebDriver driver = new ChromeDriver(options);
        
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        var baseUrl = "http://localhost:5173";

        try
        {
            // ----------------------------------------------------
            // TEST 1: User Registration UI Test (Task 06 / Scenario 1)
            // ----------------------------------------------------
            Console.WriteLine("\n[TEST 1] Testing User Registration UI...");
            driver.Navigate().GoToUrl($"{baseUrl}/register");

            var randomEmail = $"selenium.user.{Guid.NewGuid().ToString().Substring(0, 8)}@example.com";
            
            driver.FindElement(By.CssSelector("input[placeholder*='Full name'], input[name='fullName']")).SendKeys("Selenium Test User");
            driver.FindElement(By.CssSelector("input[type='email']")).SendKeys(randomEmail);
            driver.FindElement(By.CssSelector("input[placeholder*='Password'], input[name='password']")).SendKeys("Password123!");
            driver.FindElement(By.CssSelector("input[placeholder*='Confirm'], input[name='confirmPassword']")).SendKeys("Password123!");
            
            var registerBtn = driver.FindElement(By.CssSelector("button[type='submit']"));
            registerBtn.Click();
            
            wait.Until(d => d.Url.Contains("/login") || d.PageSource.Contains("registered") || d.PageSource.Contains("successful"));
            Console.WriteLine("✔ PASS: Registration UI submitted successfully and redirected!");

            // ----------------------------------------------------
            // TEST 2: User Login UI Test (Task 06 / Scenario 2)
            // ----------------------------------------------------
            Console.WriteLine("\n[TEST 2] Testing User Login UI...");
            driver.Navigate().GoToUrl($"{baseUrl}/login");

            driver.FindElement(By.CssSelector("input[type='email']")).SendKeys(randomEmail);
            driver.FindElement(By.CssSelector("input[type='password']")).SendKeys("Password123!");

            var loginBtn = driver.FindElement(By.CssSelector("button[type='submit']"));
            loginBtn.Click();

            wait.Until(d => d.Url.Contains("/buyer") || d.Url.Contains("/dashboard") || d.PageSource.Contains("Logout") || d.PageSource.Contains("Welcome"));
            Console.WriteLine("✔ PASS: Login UI authenticated successfully and navigated to Role Dashboard!");

            // ----------------------------------------------------
            // TEST 3: Role-Based Dashboard & Logout UI Test (Task 06 / Scenario 3 & 4)
            // ----------------------------------------------------
            Console.WriteLine("\n[TEST 3] Testing Logout UI...");
            var logoutBtn = driver.FindElement(By.XPath("//button[contains(text(), 'Logout') or contains(text(), 'Sign out')]"));
            logoutBtn.Click();

            wait.Until(d => d.Url.Contains("/login") || d.PageSource.Contains("Login"));
            Console.WriteLine("✔ PASS: Logout UI executed successfully!");

            Console.WriteLine("\n🎉 ALL SELENIUM AUTOMATED UI TESTS PASSED SUCCESSFULLY!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SELENIUM TEST FAILED: {ex.Message}");
        }
        finally
        {
            driver.Quit();
        }
    }
}
