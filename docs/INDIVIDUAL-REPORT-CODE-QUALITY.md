# INDIVIDUAL CONTRIBUTION REPORT
## Code Quality & Metrics (Static Analysis, Coverage & SonarCloud)

**Student ID:** IT24101852  
**Student Name:** V.N.Thilanka  
**Student Email:** it24101852@my.sliit.lk  
**GitHub Username:** IT24101852  
**Module:** SE3112 – Advanced Software Engineering | Take-Home Assessment  
**Project:** TNT Online Supermarket (SE3022 Microservice Base Code)  
**Scope:** .NET 10 Microservices (`Identity.Api`, `Catalog.Api`, `Order.Api`, `Reporting.Api`) & `Marketflow.UnitTests`  
**Stack:** SonarCloud, Roslyn .NET Analyzers, `dotnet format`, Coverlet (OpenCover/Cobertura), ReportGenerator, GitHub Actions CI/CD  

---

### 1. Objective
Establish an automated **Code Quality & Metrics** pipeline for the TNT Supermarket microservices architecture. Continuously measure and enforce static code quality, formatting standards, code coverage (line & branch), cyclomatic complexity, CRAP risk scores, and SonarCloud Quality Gates across all backend services.

---

### 2. Project & Source References

| Resource | Location / Reference |
| :--- | :--- |
| **Student ID / Author** | `IT24101852` — **V.N.Thilanka** (`it24101852@my.sliit.lk`) |
| **Feature Branch** | `feature/se3112-code-quality-metrics` |
| **Dedicated CI Workflow** | `.github/workflows/code-quality.yml` (`Code Quality & Metrics — SonarCloud`) |
| **Roslyn Analyzer Config** | `Directory.Build.props` (`AnalysisLevel=latest`, `AnalysisMode=Recommended`) |
| **Coverage Config** | `coverlet.runsettings` (Dual `cobertura` + `opencover` output) |
| **SonarCloud Config** | `sonar-project.properties` |
| **One-Click Local Runner** | `run_code_quality.cmd` / `scripts/run-code-quality.ps1` |
| **Technical & VIVA Guide** | `docs/CODE-QUALITY-AND-METRICS.md` |
| **Interactive HTML Report** | `QualityReport/index.html` (generated locally & uploaded as CI artifact) |

---

### 3. Implemented Quality Layers
1. **Compile-Time Static Analysis (`Directory.Build.props`):** Enabled `EnableNETAnalyzers`, `AnalysisLevel=latest`, `AnalysisMode=Recommended`, and `EnforceCodeStyleInBuild` across the entire `Marketflow.slnx` solution.
2. **Code Formatting Verification (`dotnet format`):** Automated verification of C# formatting rules in both local runner and CI Stage 1.
3. **Dual-Format Coverage Collection (`coverlet.runsettings`):** Configured Coverlet to exclude generated EF Core migrations and boilerplate `Program` entrypoints while emitting **OpenCover XML** (for SonarCloud) and **Cobertura XML** (for ReportGenerator).
4. **Complexity & CRAP Dashboard (`ReportGenerator`):** Generates a full interactive HTML dashboard reporting Line Coverage, Branch Coverage, Method Coverage, Cyclomatic Complexity, and CRAP scores.
5. **SonarCloud & 3-Stage GitHub Actions Pipeline (`.github/workflows/code-quality.yml`):**
   - **Stage 1:** `Static Code Analysis & Style Check`
   - **Stage 2:** `Coverage, Complexity & SonarCloud Scan`
   - **Stage 3:** `Quality Gate & Metrics Summary` (publishes live Markdown metrics table to GitHub Step Summary and uploads `code-quality-html-report` artifact).
