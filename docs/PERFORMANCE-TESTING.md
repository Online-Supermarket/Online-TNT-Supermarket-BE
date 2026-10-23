# Performance & Load Testing Documentation — TNT Supermarket (Marketflow)

## 1. Overview & Objective

As part of the **SE3112 Advanced Software Engineering** Microservice Testing Pipeline assessment, **Performance & Load Testing** has been implemented to evaluate the scalability, throughput, latency, and fault-tolerance of the **TNT Supermarket (Marketflow)** microservices architecture under high concurrent user workloads.

In a microservices ecosystem, individual services might function flawlessly under single-user tests. However, under heavy concurrency, cross-service communication, database connection pool exhaustion, API gateway serialization, and CPU bottlenecks can cause cascading failures. 

**Core Objectives:**
1. **Concurrency Validation:** Simulate **110 concurrent virtual users** executing realistic workflows.
2. **Gateway & Routing Resilience:** Validate that the API Gateway efficiently dispatches requests without dropping packets or exceeding timeout limits.
3. **Response Time Benchmarking:** Measure average response times and the critical **95th percentile (p95)** latency.
4. **Error Rate Monitoring:** Ensure HTTP error rates remain at **0.00%** (zero HTTP 5xx errors or timeouts).

---

## 2. Tooling Architecture

- **Engine:** [Apache JMeter 5.6.3](https://jmeter.apache.org/) (Industry-standard performance & load testing engine).
- **Execution Mode:** Non-GUI / Headless CLI execution (resource-efficient, suitable for CI/CD pipelines).
- **Test Plan:** `TNT_Supermarket_Performance_Test.jmx`
- **Target Gateway:** `http://localhost:8080` (Directing traffic across all microservices).
- **Reporting:** Interactive HTML Dashboard with APDEX scores, Response Time Over Time charts, and Throughput graphs.

---

## 3. Workload Configuration & Test Profile

| Parameter | Value | Rationale |
|---|---|---|
| **Virtual Users (Threads)** | **110 Concurrent Users** | Simulates peak supermarket shopping traffic |
| **Ramp-up Period** | **10 Seconds** | Smooth user ramp-up (~11 users/sec) to avoid socket flood |
| **Loop Count** | **5 Iterations** | Sustained traffic generation per thread |
| **Think Time (Timer)** | **100ms - 300ms (Uniform Random)** | Simulates realistic human pacing between consecutive requests |
| **Response Assertions** | **HTTP 200 + Payload Validation** | Validates HTTP status code and response body integrity |
| **Duration SLA Assertion** | **< 1500ms** | Enforces non-negotiable performance SLA on all endpoints |
| **Total Test Samplers** | **8 Endpoints** | Full coverage of read, write, and authentication paths |
| **Total Requests Generated** | **4,400 Requests** | 110 users × 5 loops × 8 endpoints |

### Endpoints & Assertions Covered:
1. `01 - Gateway Health Check`: `GET /health` (Response Assertion: verifies `"healthy"`)
2. `02 - Identity Service Health`: `GET /api/identity/health` (Global HTTP 200 & SLA Assertion)
3. `03 - Catalog Service Health`: `GET /api/catalog/health` (Global HTTP 200 & SLA Assertion)
4. `04 - Browse Products Catalog`: `GET /api/catalog/products` (Response Assertion: verifies JSON list `[`)
5. `05 - Browse Categories`: `GET /api/catalog/categories` (Global HTTP 200 & SLA Assertion)
6. `06 - Order Service Health`: `GET /api/order/health` (Global HTTP 200 & SLA Assertion)
7. `07 - Reporting Service Health`: `GET /api/reporting/health` (Global HTTP 200 & SLA Assertion)
8. `08 - Filter Products Catalog (Token Protected)`: `GET /api/catalog/products?page=1...` (JWT Bearer Token validation)

---

## 4. How to Run the Tests

### Option A: One-Click Execution (Recommended for Live Demo)
Run the root batch file:
```cmd
run_performance_test.bat
```
*This cleans previous results, runs the 110-user load test in headless mode, and automatically opens the interactive HTML report in your default browser.*

### Option B: PowerShell CLI (Customizable Parameters)
```powershell
# Default run (110 users, 10s ramp-up, opens report)
.\scripts\run-performance-tests.ps1 -OpenReport

# Custom stress test scenario (e.g., 200 users, 15s ramp-up)
.\scripts\run-performance-tests.ps1 -Users 200 -RampUp 15 -OpenReport
```

### Option C: Apache JMeter GUI (Visual Inspection)
1. Launch `jmeter.bat`.
2. Open `TNT_Supermarket_Performance_Test.jmx`.
3. Inspect Thread Group and HTTP Samplers.

---

## 5. Key Quality Indicators & Metrics

- **Throughput (Requests / Second):** Measures the rate at which the API Gateway and backend services process incoming requests.
- **Average Response Time:** Target is `< 200ms` for microservice gateway routing.
- **95th Percentile (p95):** The threshold below which 95% of all requests completed. Protects against skewed averages caused by extreme outliers.
- **Error Percentage:** Target is `0.00%`.
- **APDEX (Application Performance Index):** Quantifies user satisfaction (T = 500ms, F = 1500ms).

---

## 6. VIVA Defense Cheat Sheet

### Q1: Why test through the API Gateway rather than individual service ports?
**Answer:** In production, clients only interact with the system via the API Gateway. Testing via the Gateway captures realistic network serialization, route matching, JWT validation overhead, and reverse proxy latency.

### Q2: What is the difference between average response time and 95th percentile (p95)?
**Answer:** Averages can hide severe performance degradation (e.g., 99 requests take 10ms, but 1 request takes 10,000ms; average is ~109ms, masking a critical timeout). The 95th percentile ensures that 95% of users receive an optimal experience and isolates edge-case latencies.

### Q3: How do you identify a microservice bottleneck during load testing?
**Answer:** When increasing concurrent users results in a flat throughput curve (RPS ceiling) accompanied by an exponential increase in response time and error rates (timeouts/503), the service has reached its saturation point (often due to DB connection pool limits, synchronous blocking I/O, or CPU limits).
