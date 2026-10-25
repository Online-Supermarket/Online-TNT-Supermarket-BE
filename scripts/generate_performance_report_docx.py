import os
import docx
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_ALIGN_VERTICAL
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

DOCX_OUTPUT_DOCS = r"c:\Users\user\OneDrive - Sri Lanka Institute of Information Technology\Desktop\TNT-SuperMarket\Online-TNT-Supermarket-BE\docs\SE3112_JMeter_Performance_Testing_Report.docx"
DOCX_OUTPUT_ROOT = r"c:\Users\user\OneDrive - Sri Lanka Institute of Information Technology\Desktop\TNT-SuperMarket\SE3112_JMeter_Performance_Testing_Report.docx"
SCREENSHOTS_DIR = r"c:\Users\user\OneDrive - Sri Lanka Institute of Information Technology\Desktop\TNT-SuperMarket\Online-TNT-Supermarket-BE\Performance-Screenshots"

doc = docx.Document()

# Configure Normal Style
style = doc.styles['Normal']
style.font.name = 'Calibri'
style.font.size = Pt(11)
style.font.color.rgb = RGBColor(31, 41, 55) # Dark Gray

def set_cell_background(cell, fill_hex):
    tcPr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement('w:shd')
    shd.set(qn('w:val'), 'clear')
    shd.set(qn('w:color'), 'auto')
    shd.set(qn('w:fill'), fill_hex)
    tcPr.append(shd)

def set_cell_margins(cell, top=100, bottom=100, left=150, right=150):
    tcPr = cell._tc.get_or_add_tcPr()
    tcMar = OxmlElement('w:tcMar')
    for m, val in [('w:top', top), ('w:bottom', bottom), ('w:left', left), ('w:right', right)]:
        node = OxmlElement(m)
        node.set(qn('w:w'), str(val))
        node.set(qn('w:type'), 'dxa')
        tcMar.append(node)
    tcPr.append(tcMar)

def add_heading_1(text):
    p = doc.add_paragraph()
    p.paragraph_format.space_before = Pt(16)
    p.paragraph_format.space_after = Pt(6)
    p.paragraph_format.keep_with_next = True
    run = p.add_run(text)
    run.font.name = 'Calibri'
    run.font.size = Pt(16)
    run.font.bold = True
    run.font.color.rgb = RGBColor(15, 41, 66) # Deep Navy
    return p

def add_heading_2(text):
    p = doc.add_paragraph()
    p.paragraph_format.space_before = Pt(12)
    p.paragraph_format.space_after = Pt(4)
    p.paragraph_format.keep_with_next = True
    run = p.add_run(text)
    run.font.name = 'Calibri'
    run.font.size = Pt(13)
    run.font.bold = True
    run.font.color.rgb = RGBColor(29, 78, 137) # Accent Blue
    return p

def add_body(text, bold_prefix=None):
    p = doc.add_paragraph()
    p.paragraph_format.space_after = Pt(6)
    p.paragraph_format.line_spacing = 1.15
    if bold_prefix:
        r_pre = p.add_run(bold_prefix)
        r_pre.font.bold = True
    p.add_run(text)
    return p

def add_table_data(headers, data, col_widths=None):
    tbl = doc.add_table(rows=len(data) + 1, cols=len(headers))
    tbl.alignment = WD_TABLE_ALIGNMENT.CENTER
    tbl.autofit = False

    hdr = tbl.rows[0].cells
    for i, h in enumerate(headers):
        hdr[i].text = h
        set_cell_background(hdr[i], "0F2942")
        set_cell_margins(hdr[i], top=120, bottom=120, left=140, right=140)
        p = hdr[i].paragraphs[0]
        for run in p.runs:
            run.font.bold = True
            run.font.size = Pt(9.5)
            run.font.color.rgb = RGBColor(255, 255, 255)

    for r_idx, row_data in enumerate(data):
        row_cells = tbl.rows[r_idx + 1].cells
        bg = "F9FAFB" if r_idx % 2 == 1 else "FFFFFF"
        for c_idx, val in enumerate(row_data):
            row_cells[c_idx].text = str(val)
            set_cell_background(row_cells[c_idx], bg)
            set_cell_margins(row_cells[c_idx], top=90, bottom=90, left=140, right=140)
            p = row_cells[c_idx].paragraphs[0]
            for run in p.runs:
                run.font.size = Pt(9.5)

    if col_widths:
        for row in tbl.rows:
            for idx, width in enumerate(col_widths):
                row.cells[idx].width = Inches(width)

    p_after = doc.add_paragraph()
    p_after.paragraph_format.space_after = Pt(6)

def add_figure(image_filename, caption_text):
    img_path = os.path.join(SCREENSHOTS_DIR, image_filename)
    if os.path.exists(img_path):
        p_img = doc.add_paragraph()
        p_img.alignment = WD_ALIGN_PARAGRAPH.CENTER
        p_img.paragraph_format.space_before = Pt(8)
        p_img.paragraph_format.space_after = Pt(4)
        run_img = p_img.add_run()
        run_img.add_picture(img_path, width=Inches(6.0))

        p_cap = doc.add_paragraph()
        p_cap.alignment = WD_ALIGN_PARAGRAPH.CENTER
        p_cap.paragraph_format.space_after = Pt(12)
        r_cap = p_cap.add_run(caption_text)
        r_cap.font.size = Pt(9)
        r_cap.font.italic = True
        r_cap.font.color.rgb = RGBColor(107, 114, 128)

# Title & Metadata
p_title = doc.add_paragraph()
p_title.paragraph_format.space_before = Pt(0)
p_title.paragraph_format.space_after = Pt(4)
r_title = p_title.add_run("SE3112: Advanced Software Engineering — Take-Home Assessment")
r_title.font.size = Pt(18)
r_title.font.bold = True
r_title.font.color.rgb = RGBColor(15, 41, 66)

p_sub = doc.add_paragraph()
p_sub.paragraph_format.space_after = Pt(16)
r_sub = p_sub.add_run("Individual Contribution Report: Load & Performance Testing Pipeline")
r_sub.font.size = Pt(14)
r_sub.font.color.rgb = RGBColor(29, 78, 137)

meta_data = [
    ["Student ID", "IT24101022"],
    ["Student Email", "it24101022@my.sliit.lk"],
    ["Module", "SE3112 - Advanced Software Engineering (BSc Hons in CS - Y3S1)"],
    ["Project Base", "TNT Supermarket (Marketflow Cloud-Native Microservices)"],
    ["Assigned Paradigm", "Load & Performance Testing (Apache JMeter 5.6.3)"],
    ["Target Scope", "API Gateway (8080) & Microservices (Identity, Catalog, Order, Reporting)"],
    ["Dedicated CI/CD Workflow", "Load & Performance Testing — JMeter (.github/workflows/performance-testing.yml)"],
    ["Unified CI Pipeline", "CI — TNT Supermarket (.github/workflows/ci.yml)"],
    ["Feature Branch", "feature/se3112-performance-jmeter"]
]
add_table_data(["Metadata Attribute", "Configuration Details"], meta_data, col_widths=[2.5, 4.0])

# 1. Executive Summary
add_heading_1("1. Executive Summary & Testing Objectives")
add_body("In a cloud-native microservices architecture, individual services might function flawlessly under unit and functional tests. However, under high concurrent traffic, API gateway routing serialization, database connection pool exhaustion, and inter-service latency can cause catastrophic cascading failures.")
add_body("The primary objective of this role is to implement a comprehensive Load & Performance Testing pipeline using Apache JMeter 5.6.3 to evaluate system scalability, throughput, latency percentiles, and SLA adherence under peak e-commerce shopping traffic (110 concurrent virtual users).")

# 2. Workload Modeling
add_heading_1("2. Workload Modeling & Concurrency Strategy")
add_body("To simulate real-world e-commerce flash sales, the performance test was modeled with realistic concurrency, ramp-up pacing, and think-time intervals:")

workload_table = [
    ["Virtual Users (Threads)", "110 Concurrent Users", "Simulates simultaneous active shoppers during peak supermarket sales."],
    ["Ramp-up Period", "10 Seconds (~11 users/sec)", "Linear ramp-up avoiding unnatural TCP SYN floods and socket starvation."],
    ["Loop Iterations", "5 Loops per Thread", "Sustained traffic generation ensuring caches and connection pools are warm."],
    ["Think Time (Pacing)", "100ms - 300ms Uniform Random", "Simulates realistic human behavioral delays between page clicks."],
    ["Total Test Samplers", "8 Distinct Endpoints", "Comprehensive coverage of Health, Read-Heavy, and Auth-Protected routes."],
    ["Total Requests Dispatched", "4,400 Total Samples", "110 users × 5 loops × 8 samplers = 4,400 total HTTP transactions."],
    ["Execution Mode", "Headless Non-GUI CLI", "Eliminates client-side GUI resource contention for pristine latency measurement."]
]
add_table_data(["Parameter", "Value", "Technical Rationale"], workload_table, col_widths=[1.8, 1.8, 2.9])

# 3. Quality Assertions & SLA
add_heading_1("3. Quality Assertions & Performance SLA Criteria")
add_body("To achieve maximum implementation depth and evaluate system resilience, dual-layer quality assertions were enforced across all 8 samplers:")
add_body("Every HTTP request is verified against HTTP Status 200 OK, and failure to return 200 immediately triggers a test failure.", "1. Global HTTP 200 Status Assertion: ")
add_body("A strict Service Level Agreement of latency < 1,500ms is enforced. Any sample exceeding 1.5 seconds is recorded as an SLA violation.", "2. Strict SLA Duration Assertion (< 1500ms): ")
add_body("Gateway health responses are inspected for the 'healthy' status token, and Catalog browse endpoints are asserted for valid JSON array structure ('[').", "3. Payload Structure & Health Assertions: ")
add_body("Secured endpoints utilize signed JWT Bearer session tokens to validate authentication introspection under high concurrency.", "4. JWT Bearer Token Authentication: ")

# Endpoints Table
add_heading_2("Target Endpoints Matrix Under Test")
endpoints_table = [
    ["01", "Gateway Health Probe", "GET", "/health", "HTTP 200, SLA < 1.5s, Payload contains 'healthy'"],
    ["02", "Identity Service Health", "GET", "/api/identity/health", "HTTP 200, SLA < 1.5s, Identity DB ready"],
    ["03", "Catalog Service Health", "GET", "/api/catalog/health", "HTTP 200, SLA < 1.5s, Catalog DB ready"],
    ["04", "Browse Products Catalog", "GET", "/api/catalog/products", "HTTP 200, SLA < 1.5s, Valid JSON array payload"],
    ["05", "Browse Categories", "GET", "/api/catalog/categories", "HTTP 200, SLA < 1.5s, Hierarchy read-path"],
    ["06", "Order Service Health", "GET", "/api/order/health", "HTTP 200, SLA < 1.5s, Order DB ready"],
    ["07", "Reporting Projection Health", "GET", "/api/reporting/health", "HTTP 200, SLA < 1.5s, Kafka projection check"],
    ["08", "Filter Products (Auth)", "GET", "/api/catalog/products?page=1...", "HTTP 200, SLA < 1.5s, Bearer token validated"]
]
add_table_data(["#", "Sampler Name", "Method", "Target Route", "Enforced Quality Check"], endpoints_table, col_widths=[0.4, 1.8, 0.7, 1.8, 1.8])

# 4. Results & Dashboard Analysis
add_heading_1("4. Benchmark Results & Dashboard Analysis")
add_body("The performance test suite executed in headless non-GUI mode generating raw JTL telemetry (jmeter_results.jtl) and an interactive Apache JMeter HTML Dashboard:")

results_table = [
    ["Total HTTP Requests Dispatched", "4,400 Requests"],
    ["Successful Transactions", "4,400 Passed (100.0%)"],
    ["SLA Violations / Assertions Failed", "0 (0.00% Error Rate)"],
    ["Aggregate System Throughput", "440.97 Requests / Second"],
    ["Median Response Time (p50)", "7.00 ms"],
    ["Average (Mean) Response Time", "10.77 ms"],
    ["90th Percentile Latency (p90)", "20.00 ms"],
    ["95th Percentile Latency (p95)", "28.00 ms (Far superior to 1,500ms SLA)"],
    ["99th Percentile Latency (p99)", "60.00 ms"],
    ["APDEX User Satisfaction Rating", "0.98+ (Excellent - T=500ms, F=1500ms)"]
]
add_table_data(["Telemetry Metric", "Recorded Benchmark Value"], results_table, col_widths=[3.2, 3.3])

add_figure("01_jmeter_dashboard_overview.png", "Figure 1: Apache JMeter Interactive HTML Dashboard — APDEX Score (0.98+) & Summary")
add_figure("02_jmeter_statistics_table.png", "Figure 2: Performance Telemetry Statistics Table across all 8 microservice endpoints")
add_figure("03_jmeter_response_time_over_time.png", "Figure 3: Response Time Over Time — Latency stability under 110 concurrent threads")
add_figure("04_jmeter_throughput_tps.png", "Figure 4: Throughput & Transactions Per Second (Peak 440+ req/s)")
add_figure("05_jmeter_response_time_percentiles.png", "Figure 5: Response Time Percentile Distribution (95% of requests completed under 28ms)")

# 5. CI/CD Integration
add_heading_1("5. CI/CD Pipeline Automation & Individual Proof of Work (GitHub Actions)")
add_body("In strict adherence to the Group Requirement (Criterion 1: Test Automation & Pipeline), Apache JMeter was fully integrated into GitHub Actions CI/CD workflows under Student ID IT24101022 on branch feature/se3112-performance-jmeter.")
add_body("Key Highlights of the CI/CD Implementation:")
add_body("• Dedicated 3-Stage Standalone Workflow: Configured .github/workflows/performance-testing.yml featuring a 3-node connected visual pipeline (Validate JMeter Test Plan -> JMeter Load Test -> Performance Test Summary). Executed in only 2m 1s with 100% green status.", None)
add_body("• Centralized Group CI Pipeline: Fully integrated into .github/workflows/ci.yml as a core gate ('Load tests (JMeter smoke & performance)'), connecting with Backend Unit Tests, Stryker Mutation Tests, and Selenium E2E Tests.", None)
add_body("• Automated Telemetry Step Summaries: Each run formats an in-pipeline Markdown table detailing throughput, ramp-up time, and SLA pass rates directly on the GitHub Actions summary page.", None)
add_body("• Downloadable Test Artifacts: The raw JTL telemetry and complete HTML dashboard are packaged and uploaded as workflow artifacts on every execution.", None)

add_figure("06_github_actions_jmeter_pipeline.png", "Figure 6: GitHub Actions 3-Stage Connected Pipeline & Summary Table (Run #1: Success by IT24101022 in 2m 1s)")
add_figure("07_github_commits_it24101022.png", "Figure 7: GitHub Feature Branch Commits and CI Verification by IT24101022 on feature/se3112-performance-jmeter")

# 6. Live Demo Plan
add_heading_1("6. Two-Minute Live Demo & Walkthrough Plan")
demo_table = [
    ["0:00 - 0:25", "Role Introduction", "Explain load testing purpose in microservices (preventing gateway bottlenecks & connection pool exhaustion) and present 110 concurrent users profile."],
    ["0:25 - 0:50", "Live Script Execution", "Double-click run_performance_test.bat live, explaining why headless non-GUI mode is necessary to eliminate client-side UI overhead."],
    ["0:50 - 1:20", "Interactive Dashboard", "Show jmeter_html_report/index.html in browser. Highlight APDEX score (0.98+) and aggregate throughput of 440 requests/sec."],
    ["1:20 - 1:45", "Percentiles & SLA Adherence", "Explain that Median is 7ms and 95th Percentile is 28ms, proving strict compliance with the 1,500ms SLA without a single timeout."],
    ["1:45 - 2:00", "CI/CD Pipeline Integration", "Present GitHub Actions Workflow Run by IT24101022 showing the clean 3-stage green Success check on 'Load & Performance Testing — JMeter'."]
]
add_table_data(["Timeline", "Presentation Stage", "Action & Key Verbal Talking Points"], demo_table, col_widths=[1.2, 1.8, 3.5])

# 7. VIVA Preparation
add_heading_1("7. VIVA Concept Mastery & Technical Q&A")
viva_table = [
    ["Why run JMeter in Non-GUI mode?", "The GUI consumes substantial CPU/RAM to render UI listeners, which artificially skews latency results under high concurrency. Non-GUI CLI generates pristine JTL metrics."],
    ["Why 95th Percentile (p95) over Average?", "Averages conceal extreme latency spikes. The 95th percentile guarantees that 95% of user requests completed in 28ms or faster, which is critical for real-world SLAs."],
    ["What does an APDEX of 0.98 mean?", "Application Performance Index measures user satisfaction on a 0 to 1 scale. With T=500ms and F=1500ms, 98%+ of requests were instantaneously satisfied."],
    ["Why use a Uniform Random Timer (Think Time)?", "Real shoppers pause between clicks. Sending uninterrupted requests simulates an unrealistic DDoS attack. Think time realistically spaces requests to measure true capacity."],
    ["How did you test token-secured endpoints?", "I configured an HTTP Header Manager passing 'Authorization: Bearer ${AUTH_TOKEN}' with a signed JWT session token from the Identity microservice."]
]
add_table_data(["Examiner VIVA Question", "Technical & Authoritative Answer"], viva_table, col_widths=[2.5, 4.0])

# 8. Deliverables & File Locations
add_heading_1("8. Deliverables & File Locations Summary")
files_table = [
    ["JMeter Test Plan", "TNT_Supermarket_Performance_Test.jmx", "Core test plan with 8 samplers, assertions, think time"],
    ["Standalone CI Workflow", ".github/workflows/performance-testing.yml", "Dedicated 3-stage GitHub Actions pipeline (Run #1: Success by IT24101022)"],
    ["Centralized CI Pipeline", ".github/workflows/ci.yml", "Full CI pipeline gate for JMeter load testing"],
    ["One-Click Local Runner", "run_performance_test.bat", "Double-click script to run test & open HTML dashboard"],
    ["PowerShell CLI Script", "scripts/run-performance-tests.ps1", "Advanced script with -Users, -RampUp, -Loops params"],
    ["Interactive Dashboard", "jmeter_html_report/index.html", "Generated HTML report with APDEX and telemetry charts"],
    ["Telemetry Raw Log", "jmeter_results.jtl", "Raw execution data log (4,400 samples recorded)"],
    ["High-Res Screenshots", "Performance-Screenshots/", "Folder containing 7 high-res metric & CI snapshots for report"],
    ["HTML Slides", "docs/JMeter_Performance_Slides.html", "Interactive 2-slide presentation with speaker talking points"]
]
add_table_data(["Deliverable", "Relative File Location", "Description & Purpose"], files_table, col_widths=[1.8, 2.3, 2.4])

doc.save(DOCX_OUTPUT_DOCS)
print(f"Report successfully generated at: {DOCX_OUTPUT_DOCS}")
try:
    doc.save(DOCX_OUTPUT_ROOT)
    print(f"Report also mirrored at: {DOCX_OUTPUT_ROOT}")
except Exception as e:
    print(f"Root save note: {e}")
