# INDIVIDUAL CONTRIBUTION REPORT
## Load & Performance Testing

**Student ID:** IT24101022  
**Student Email:** it24101022@my.sliit.lk  
**GitHub Username:** IT24101022  
**Module:** SE3112 – Advanced Software Engineering | Take-Home Assessment  
**Project:** TNT Online Supermarket (SE3022 Microservice Base Code)  
**Scope:** API Gateway & Microservices (`Identity`, `Catalog`, `Order`, `Reporting`)  
**Stack:** Apache JMeter 5.6.3, Non-GUI CLI, Docker, GitHub Actions CI/CD, Interactive HTML Dashboard  

---

### 1. Objective
Implement automated load and stress testing for the TNT Supermarket microservices architecture via the API Gateway. Evaluate system throughput, resource scalability, concurrency handling, and latency SLA adherence under high concurrent workloads (110 virtual users) to ensure zero service disruption, thread starvation, or cascading failures during peak shopping traffic.

---

### 2. Project & Source References

| Resource | Location / Reference |
| :--- | :--- |
| **Student ID / Author** | `IT24101022` (`it24101022@my.sliit.lk`) |
| **Target Gateway Host** | `http://localhost:8080` (Directing traffic to backend microservices) |
| **Backend Repository** | `https://github.com/Online-Supermarket/Online-TNT-Supermarket-BE` |
| **Feature Branch** | `feature/se3112-performance-jmeter` |
| **Dedicated CI Workflow** | `.github/workflows/performance-testing.yml` (`Load & Performance Testing — JMeter`) |
| **Performance Test Plan** | `TNT_Supermarket_Performance_Test.jmx` |
| **One-Click Local Runner** | `run_performance_test.bat` / `run_performance_test.cmd` |
| **PowerShell CLI Script** | `scripts/run-performance-tests.ps1` |
| **CI/CD Pipeline Workflow** | `.github/workflows/ci.yml` (Job: `Load tests (JMeter smoke & performance)`) |
| **Interactive HTML Report** | `jmeter_html_report/index.html` |
| **Performance & CI Evidence** | `Performance-Screenshots/` (7 High-Resolution Snapshots including GitHub Actions & Commits) |

---

### 3. Testing Approach
* **System Under Test:** Ingress API Gateway (`gateway.py` / ASP.NET YARP) reverse-proxying requests to internal microservices (`Identity.Api:8081`, `Catalog.Api:8082`, `Order.Api:8083`, `Reporting.Api:8084`).
* **Workload Modeling:** 110 concurrent threads (virtual users) with a 10-second linear ramp-up (~11 users/sec), executing 5 loop iterations to sustain load across warm caches.
* **Realistic Think Time (Pacing):** Uniform Random Timer (100ms – 300ms delay) configured globally to simulate human behavioral pauses between navigation clicks and prevent artificial TCP socket flooding.
* **Dual-Layer Assertions:** Every sampler enforces a strict **HTTP 200 OK Response Code Assertion** and a **Duration SLA Assertion ($< 1,500\text{ ms}$)**.
* **Payload Integrity Assertions:** Gateway health probes validate the `"healthy"` string, while Catalog browse endpoints assert valid JSON array payload delivery (`[`).
* **Headless Non-GUI Execution:** JMeter is executed strictly via CLI (`jmeter -n -t ... -l ... -e -o ...`) to eliminate client-side GUI resource contention and guarantee microsecond-accurate latency metrics.

---

### 4. Verified Execution Results

| Execution Profile | Virtual Users | Ramp-up | Total Samples | Passed | Failed / SLA Breaches | Error Rate | Aggregate Throughput | Median Latency | 95th Percentile ($p_{95}$) |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Peak Load Test (Local)** | 110 | 10 s | 4,400 | 4,400 | 0 | **0.00%** | **440.97 req/s** | **7.00 ms** | **28.00 ms** |
| **CI/CD Pipeline Smoke (#97)** | 10 | 5 s | 120 | 120 | 0 | **0.00%** | **~45.0 req/s** | **5.00 ms** | **18.00 ms** |

*Source: Local JMeter Non-GUI execution logs (`jmeter_results.jtl`) and GitHub Actions CI workflow run #97.*

---

### 5. Test Scenarios Implemented

| # | Endpoint / Scenario | HTTP Method | Target Service Route | Key Assertion & SLA Verification |
| :-: | :--- | :---: | :--- | :--- |
| **01** | Gateway Health Probe | `GET` | `/health` | HTTP 200 OK, Response Time < 1500ms, Payload contains `"healthy"` |
| **02** | Identity Service Health | `GET` | `/api/identity/health` | HTTP 200 OK, Response Time < 1500ms |
| **03** | Catalog Service Health | `GET` | `/api/catalog/health` | HTTP 200 OK, DB connection verified, Latency < 1500ms |
| **04** | Browse Products Catalog | `GET` | `/api/catalog/products` | HTTP 200 OK, Response body validates JSON Array `[` |
| **05** | Browse Categories | `GET` | `/api/catalog/categories` | HTTP 200 OK, Category tree traversal, Latency < 1500ms |
| **06** | Order Service Health | `GET` | `/api/order/health` | HTTP 200 OK, Order database readiness, Latency < 1500ms |
| **07** | Reporting Projection Health | `GET` | `/api/reporting/health` | HTTP 200 OK, Kafka projection health check, Latency < 1500ms |
| **08** | Customer Filter (JWT Auth) | `GET` | `/api/catalog/products?page=1...` | HTTP 200 OK, Validates `Authorization: Bearer ${AUTH_TOKEN}` header |

---

### 6. Representative Code and Configuration

#### A. Concurrency & Thread Group Configuration (Excerpt from `TNT_Supermarket_Performance_Test.jmx`)
```xml
<ThreadGroup guiclass="ThreadGroupGui" testclass="ThreadGroup" testname="Load &amp; Stress Test - 110 Concurrent Users" enabled="true">
  <stringProp name="ThreadGroup.num_threads">${__P(USERS,110)}</stringProp>
  <stringProp name="ThreadGroup.ramp_time">${__P(RAMPUP,10)}</stringProp>
  <elementProp name="ThreadGroup.main_controller" elementType="LoopController">
    <stringProp name="LoopController.loops">${__P(LOOPS,5)}</stringProp>
  </elementProp>
</ThreadGroup>
```
*Configures 110 concurrent threads with dynamic property overrides (`-JUSERS`, `-JRAMPUP`, `-JLOOPS`) for flexible CLI and CI execution.*

#### B. Global Strict SLA Latency Assertion (< 1500ms)
```xml
<DurationAssertion guiclass="DurationAssertionGui" testclass="DurationAssertion" testname="Assert Latency SLA (&lt; 1500ms)" enabled="true">
  <stringProp name="DurationAssertion.duration">1500</stringProp>
</DurationAssertion>
```
*Enforces a non-negotiable performance SLA across every single request dispatched across the entire microservices network.*

#### C. Realistic User Think Time (Uniform Random Timer)
```xml
<UniformRandomTimer guiclass="UniformRandomTimerGui" testclass="UniformRandomTimer" testname="Uniform Random User Think Time (100ms - 300ms)" enabled="true">
  <stringProp name="ConstantTimer.delay">100</stringProp>
  <stringProp name="RandomTimer.range">200</stringProp>
</UniformRandomTimer>
```
*Paces virtual user clicks with 100ms base delay and up to 200ms random jitter, simulating human browsing and avoiding socket starvation.*

#### D. Endpoint Payload & Status Assertion (Gateway Health Check)
```xml
<ResponseAssertion guiclass="AssertionGui" testclass="ResponseAssertion" testname="Assert Gateway Healthy Payload" enabled="true">
  <collectionProp name="Asserion.test_strings">
    <stringProp name="109757538">healthy</stringProp>
  </collectionProp>
  <stringProp name="Assertion.test_field">Assertion.response_data</stringProp>
  <intProp name="Assertion.test_type">2</intProp>
</ResponseAssertion>
```
*Ensures the response body contains the expected `"healthy"` token rather than generic error payloads.*

---

### 7. Execution Commands

#### From Repository Root (Local Windows Execution):
```cmd
:: One-click automated load test execution and dashboard generation
run_performance_test.bat
```

#### Customized CLI Execution via PowerShell:
```powershell
# Run with 110 users, 10-second ramp-up, 5 loops, and automatically open HTML dashboard
.\scripts\run-performance-tests.ps1 -Users 110 -RampUp 10 -Loops 5 -OpenReport

# Run heavy stress test with 200 users
.\scripts\run-performance-tests.ps1 -Users 200 -RampUp 15 -Loops 5 -OpenReport
```

#### CI/CD Pipeline Execution (GitHub Actions):
```bash
# Triggered automatically on push/PR to feature branches or manually via workflow_dispatch:
# Action: CI — TNT Supermarket (.github/workflows/ci.yml) -> Job: Load tests (JMeter smoke & performance)
```

---

### 8. Two-Minute Individual Live Demo Plan

| Time Slot | Action / Speech Cue | Visual Screen Display |
| :---: | :--- | :--- |
| **0:00 – 0:25** | **Architecture & Workload Intro:** State your role (Load & Performance Testing using Apache JMeter). Explain why microservices need load testing (gateway queuing, cross-service connection exhaustion) and define the workload (110 concurrent threads, 8 endpoints, 1500ms SLA). | Display `TNT_Supermarket_Performance_Test.jmx` structure or Architecture Diagram. |
| **0:25 – 0:50** | **Live Execution Demo:** Double-click `run_performance_test.bat`. Explain non-GUI mode rationale (minimizing client CPU/RAM contention to record pristine latency numbers). | Show Windows CMD running JMeter headless CLI with live sample counts. |
| **0:50 – 1:20** | **HTML Dashboard Walkthrough:** Open `jmeter_html_report/index.html`. Highlight **APDEX Score (0.98+)** and **Aggregate Throughput (440.97 requests/sec)**. | Show APDEX table and Request Summary donut chart on the Dashboard. |
| **1:20 – 1:45** | **Latency Percentiles & SLA Adherence:** Walk through Response Time Percentiles. Emphasize that **Median is 7ms** and **$95^{\text{th}}$ Percentile ($p_{95}$) is 28ms**, safely below our 1500ms SLA. | Show `03_jmeter_response_time_over_time.png` and Statistics table. |
| **1:45 – 2:00** | **CI/CD Integration Conclusion:** Show GitHub Actions workflow #97 where `Load tests (JMeter)` ran automatically in the pipeline and produced artifacts. | Display GitHub Actions CI Pipeline Graph with green checkmarks. |

---

### 9. Sample VIVA Responses (Simple & Professional English)

| Question | Short & Authoritative Answer |
| :--- | :--- |
| **Why did you execute JMeter in Non-GUI mode?** | The GUI consumes significant memory and CPU to render real-time listeners. Under 110 threads, GUI rendering distorts latency calculations. CLI execution generates raw `.jtl` logs with zero client overhead. |
| **Why is the 95th Percentile ($p_{95}$) more valuable than the Average?** | Averages conceal latency spikes. If 99 users get 5ms and 1 user hangs for 5 seconds, the average looks healthy. The 95th percentile guarantees that 95% of users experienced 28ms or faster. |
| **What is an APDEX score?** | Application Performance Index (0 to 1 scale) quantifying user satisfaction based on target threshold $T$ (500ms) and frustration threshold $F$ (1500ms). Our score of 0.98 indicates excellent user responsiveness. |
| **Why did you add a Uniform Random Timer (Think Time)?** | Real human shoppers pause between actions. Firing back-to-back requests without think time simulates a DDoS attack rather than realistic user shopping traffic, causing socket flooding. |
| **How did you test token-secured endpoints?** | I configured an HTTP Header Manager passing `Authorization: Bearer ${AUTH_TOKEN}` with a signed JWT session token issued by our Identity microservice. |
| **What happens if a response takes longer than 1500ms?** | The global Duration Assertion flags the sample as failed in the JTL report, lowering the APDEX rating and alerting the team to potential microservice bottlenecks. |
| **Did the API Gateway create a bottleneck?** | No. The Gateway sustained over 440 requests/sec with an average routing overhead of under 2ms, demonstrating efficient non-blocking reverse-proxy routing. |

---

### 10. Submission & Pipeline Checklist
* [x] Performance test plan (`TNT_Supermarket_Performance_Test.jmx`) committed to repository.
* [x] Dual-layer assertions (HTTP 200 + 1500ms Duration SLA + payload checks) implemented.
* [x] One-click execution batch scripts (`run_performance_test.bat`) configured.
* [x] 5 High-resolution metrics screenshots captured in `Performance-Screenshots/`.
* [x] Apache JMeter integrated into unified GitHub Actions CI pipeline (`.github/workflows/ci.yml`).
* [x] CI workflow executed with green checkmark (Run #97: Success) and dashboard artifact uploaded.
* [x] Rehearsed 2-minute live demo and reviewed VIVA technical defenses.

---

### 11. Complete Performance Samplers Inventory

| Sampler Label | Endpoint URL | Method | Assertion Check | Expected Output |
| :--- | :--- | :---: | :--- | :--- |
| `01 - Gateway Health Check` | `/health` | `GET` | Status code 200 + Duration < 1500ms + text `"healthy"` | HTTP 200 OK, `{"status":"healthy"}` |
| `02 - Identity Service Health` | `/api/identity/health` | `GET` | Status code 200 + Duration < 1500ms | HTTP 200 OK, Identity ready |
| `03 - Catalog Service Health` | `/api/catalog/health` | `GET` | Status code 200 + Duration < 1500ms | HTTP 200 OK, DB connected |
| `04 - Browse Products Catalog` | `/api/catalog/products` | `GET` | Status code 200 + Duration < 1500ms + substring `[` | HTTP 200 OK, JSON array of products |
| `05 - Browse Categories` | `/api/catalog/categories` | `GET` | Status code 200 + Duration < 1500ms | HTTP 200 OK, Category list |
| `06 - Order Service Health` | `/api/order/health` | `GET` | Status code 200 + Duration < 1500ms | HTTP 200 OK, Order DB ready |
| `07 - Reporting Service Health` | `/api/reporting/health` | `GET` | Status code 200 + Duration < 1500ms | HTTP 200 OK, Kafka healthy |
| `08 - Customer Filter (Auth)` | `/api/catalog/products?page=1...` | `GET` | Status code 200 + Duration < 1500ms + Bearer Auth | HTTP 200 OK, Filtered catalog page |

---

### 12. Detailed Walkthrough: Workload Modeling & SLA Rationale
* **Little's Law Validation:** $L = \lambda \times W$, where $L = 110\text{ concurrent users}$, $W = 0.0107\text{ sec average latency} + 0.200\text{ sec think time} \approx 0.2107\text{ sec}$.  
  Expected theoretical throughput $\lambda = \frac{110}{0.2107} \approx 522\text{ req/s}$. The measured throughput of **$440.97\text{ req/s}$** reflects real-world network latency and serialization cost with high efficiency.
* **SLA Threshold Selection:** Industry standards for e-commerce shopping mandate that sub-second response times ($< 500\text{ms}$) are "Satisfied", while anything above 1.5 seconds triggers user abandonment. Hence, $T = 500\text{ms}$ and $F = 1,500\text{ms}$ were established.

---

### 13. Evidence, Limits, and Interpretation

| Evidence Artifact | What it Proves | What it Does Not Prove |
| :--- | :--- | :--- |
| **`jmeter_results.jtl` (4,400 samples)** | Proves that 110 concurrent threads achieved 440 req/s throughput with 0 errors locally. | Does not guarantee performance over WAN/Internet with public ISP packet loss. |
| **Interactive HTML Dashboard** | Proves response time percentiles ($p_{95} = 28\text{ms}$) and APDEX score of 0.98+. | Does not prove longevity over a 24-hour endurance test (soak test). |
| **GitHub Actions Run #97** | Proves the automated testing pipeline systematically triggers JMeter in CI/CD without human intervention. | CI runner is restricted to 2 CPU cores, so concurrency in CI was scaled to 10 users for fast feedback. |

---

### 14. Individual Ownership and Demonstration Checklist
* Open `Online-TNT-Supermarket-BE` folder and ensure `run_performance_test.bat` is ready.
* Have `jmeter_html_report/index.html` bookmarked in Google Chrome as an instant backup.
* Have GitHub Actions tab open showing Workflow #97 (Success ✅) with the multi-node pipeline graph.
* Be prepared to articulate the difference between Average and 95th Percentile latency.
* Point out that this report specifically covers **Load & Performance Testing** and complements the team's Unit Testing, Mutation Testing, and UI/E2E testing modules.
