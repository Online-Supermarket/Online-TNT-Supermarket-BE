import os
import time
from selenium import webdriver
from selenium.webdriver.chrome.options import Options

OUTPUT_DIR = r"c:\Users\user\OneDrive - Sri Lanka Institute of Information Technology\Desktop\TNT-SuperMarket\Online-TNT-Supermarket-BE\docs\images\performance"
REPORT_DIR = r"c:\Users\user\OneDrive - Sri Lanka Institute of Information Technology\Desktop\TNT-SuperMarket\Online-TNT-Supermarket-BE\jmeter_html_report"

os.makedirs(OUTPUT_DIR, exist_ok=True)

chrome_opts = Options()
chrome_opts.add_argument("--headless=new")
chrome_opts.add_argument("--window-size=1600,1050")
chrome_opts.add_argument("--disable-gpu")
chrome_opts.add_argument("--no-sandbox")

driver = webdriver.Chrome(options=chrome_opts)

try:
    # 1. Main Dashboard (APDEX, Requests Summary, Statistics Table)
    index_url = "file:///" + os.path.join(REPORT_DIR, "index.html").replace("\\", "/")
    print(f"Loading {index_url}...")
    driver.get(index_url)
    time.sleep(2.5)
    p1 = os.path.join(OUTPUT_DIR, "01_jmeter_dashboard_overview.png")
    driver.save_screenshot(p1)
    print(f"[SAVED] {p1}")

    # Scroll down to capture the Statistics Table in full view
    driver.execute_script("window.scrollTo(0, 650);")
    time.sleep(1.5)
    p2 = os.path.join(OUTPUT_DIR, "02_jmeter_statistics_table.png")
    driver.save_screenshot(p2)
    print(f"[SAVED] {p2}")

    # 2. Charts Over Time (Response Time Over Time)
    over_time_url = "file:///" + os.path.join(REPORT_DIR, "content", "pages", "OverTime.html").replace("\\", "/")
    print(f"Loading {over_time_url}...")
    driver.get(over_time_url)
    time.sleep(2.5)
    p3 = os.path.join(OUTPUT_DIR, "03_jmeter_response_time_over_time.png")
    driver.save_screenshot(p3)
    print(f"[SAVED] {p3}")

    # 3. Throughput Charts (Hits Per Second & Transactions Per Second)
    throughput_url = "file:///" + os.path.join(REPORT_DIR, "content", "pages", "Throughput.html").replace("\\", "/")
    print(f"Loading {throughput_url}...")
    driver.get(throughput_url)
    time.sleep(2.5)
    p4 = os.path.join(OUTPUT_DIR, "04_jmeter_throughput_tps.png")
    driver.save_screenshot(p4)
    print(f"[SAVED] {p4}")

    # 4. Response Time Percentiles
    resp_times_url = "file:///" + os.path.join(REPORT_DIR, "content", "pages", "ResponseTimes.html").replace("\\", "/")
    print(f"Loading {resp_times_url}...")
    driver.get(resp_times_url)
    time.sleep(2.5)
    p5 = os.path.join(OUTPUT_DIR, "05_jmeter_response_time_percentiles.png")
    driver.save_screenshot(p5)
    print(f"[SAVED] {p5}")

finally:
    driver.quit()
    print("Screenshot capture completed successfully!")
