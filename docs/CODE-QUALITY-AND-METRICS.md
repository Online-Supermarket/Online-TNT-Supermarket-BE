# Code Quality & Metrics Documentation — TNT Supermarket (Marketflow)

**Owner:** `IT24101852` — **V.N.Thilanka** (`it24101852@my.sliit.lk`)  
**Module:** SE3112 — Advanced Software Engineering (Microservice Testing Pipeline)

---

## 1. Overview & Objective

To guarantee long-term maintainability, low technical debt, and verifiable test coverage across the **TNT Supermarket (Marketflow)** .NET 10 microservices (`Identity.Api`, `Catalog.Api`, `Order.Api`, `Reporting.Api`), an automated **Code Quality & Static Analysis Pipeline** has been implemented.

**Core Objectives:**
1. **Static Code Analysis:** Automatically detect code smells, security hotspots, maintainability issues, and style violations during every build via **Roslyn .NET Analyzers** and **SonarCloud**.
2. **Code Coverage Telemetry:** Collect **Line Coverage**, **Branch Coverage**, and **Method Coverage** via **Coverlet** in both **OpenCover** (for SonarCloud) and **Cobertura** (for ReportGenerator) formats.
3. **Complexity & Risk Metrics:** Measure **Cyclomatic Complexity** and **CRAP (Change Risk Anti-Patterns) Score** per class/method using **ReportGenerator**'s interactive HTML dashboard.
4. **Automated Quality Gate:** Enforce automated checks in GitHub Actions (`.github/workflows/code-quality.yml`) on every push and pull request.

---

## 2. Tooling Architecture

| Layer | Tool / Standard | Purpose |
|---|---|---|
| **Cloud Quality & Technical Debt** | **SonarCloud** (`sonar-project.properties`) | Code smells, bugs, vulnerabilities, duplications, and Quality Gate |
| **Compiler-Level Static Analysis** | **Roslyn .NET Analyzers** (`Directory.Build.props`) | Compile-time `CA*` / `IDE*` rules (`AnalysisLevel=latest`, `AnalysisMode=Recommended`) |
| **Style & Formatting Verification** | **`dotnet format`** | Enforces consistent C# formatting and code style rules |
| **Coverage Collector** | **Coverlet** (`coverlet.runsettings`) | Collects OpenCover + Cobertura coverage from xUnit (`Marketflow.UnitTests`) |
| **Interactive Metrics Dashboard** | **ReportGenerator** | Generates `QualityReport/index.html` with Cyclomatic Complexity & CRAP Scores |
| **CI/CD Automation** | **GitHub Actions** (`.github/workflows/code-quality.yml`) | 3-stage pipeline with GitHub Step Summary & HTML report artifact |

---

## 3. How to Run Locally (Live Demo)

### Option A: One-Click Runner (Recommended for Live Demo)
```cmd
run_code_quality.cmd
```

### Option B: PowerShell CLI
```powershell
# Run Static Analysis, Coverlet Coverage & open HTML Dashboard
.\scripts\run-code-quality.ps1 -OpenReport

# Run with a strict Quality Gate threshold (e.g., minimum 20% line coverage)
.\scripts\run-code-quality.ps1 -MinCoveragePercent 20 -OpenReport

# Run with SonarCloud upload (requires SONAR_TOKEN environment variable)
$env:SONAR_TOKEN = "your_sonarcloud_token"
.\scripts\run-code-quality.ps1 -RunSonarCloud -OpenReport
```

---

## 4. SonarCloud Setup Guide (GitHub Actions)

1. Log in to [SonarCloud](https://sonarcloud.io/) using GitHub and import **`Online-Supermarket/Online-TNT-Supermarket-BE`**.
2. Verify `sonar-project.properties` matches your SonarCloud **Organization** (`online-supermarket`) and **Project Key** (`Online-Supermarket_Online-TNT-Supermarket-BE`).
3. In SonarCloud: **Administration → Analysis Method**, disable *Automatic Analysis* (since CI-based analysis is used).
4. In GitHub Repository: **Settings → Secrets and variables → Actions → New repository secret**:
   - Name: `SONAR_TOKEN`
   - Value: Your generated SonarCloud token.
5. Push or trigger **Code Quality & Metrics — SonarCloud** in the GitHub Actions tab.

---

## 5. VIVA Defense Cheat Sheet

### Q1: What is the difference between Line Coverage and Branch Coverage?
**Answer:** **Line Coverage** measures the percentage of executable code lines executed by tests. **Branch Coverage** measures whether every decision path (`if`/`else`, `switch`, ternary `?:`, `&&`/`||`) was evaluated as both `true` and `false`. Branch coverage is stricter because a single line with `if (a && b)` can show 100% line coverage while leaving edge branches untested.

### Q2: What is Cyclomatic Complexity and CRAP Score?
**Answer:** **Cyclomatic Complexity** counts the number of linearly independent paths through a method's control flow graph (higher complexity = harder to test and maintain). **CRAP (Change Risk Anti-Patterns) Score** combines Cyclomatic Complexity with Test Coverage: complex methods with low test coverage receive a high CRAP score, signaling high regression risk.

### Q3: Why combine Roslyn Analyzers + Coverlet + SonarCloud instead of only one tool?
**Answer:** **Roslyn Analyzers** give immediate compile-time feedback locally in the IDE/CLI without needing internet access. **Coverlet + ReportGenerator** generate deterministic OpenCover/Cobertura coverage and complexity dashboards. **SonarCloud** aggregates these metrics over time, tracks Technical Debt, code duplication, and enforces PR Quality Gates.
