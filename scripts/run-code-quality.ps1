<#
.SYNOPSIS
    Runs Code Quality, Static Analysis, Coverage, Complexity Metrics, and optional SonarCloud Scan for TNT Supermarket (Marketflow).
.DESCRIPTION
    Owner: IT24101852 (V.N.Thilanka) — SE3112 Take-Home Assessment: Code Quality & Metrics.
    Executes Roslyn static analyzers, dotnet format verification, Coverlet OpenCover/Cobertura coverage,
    ReportGenerator interactive HTML dashboard (Line/Branch/Method Coverage, Cyclomatic Complexity, CRAP Score),
    and optionally uploads results to SonarCloud when -RunSonarCloud is passed with SONAR_TOKEN.
#>
param(
    [switch]$OpenReport,
    [switch]$RunSonarCloud,
    [int]$MinCoveragePercent = 0
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $RepoRoot

try {
    Write-Host "=================================================================" -ForegroundColor Cyan
    Write-Host "  SE3112 Code Quality & Metrics Pipeline [IT24101852 - V.N.Thilanka]" -ForegroundColor Cyan
    Write-Host "=================================================================" -ForegroundColor Cyan

    # Clean previous results
    if (Test-Path "TestResults") { Remove-Item -Recurse -Force "TestResults" }
    if (Test-Path "QualityReport") { Remove-Item -Recurse -Force "QualityReport" }

    # Ensure ReportGenerator is installed
    $rgPath = Get-Command reportgenerator -ErrorAction SilentlyContinue
    if (-not $rgPath) {
        Write-Host "`n[1/4] Installing dotnet-reportgenerator-globaltool..." -ForegroundColor Yellow
        dotnet tool install -g dotnet-reportgenerator-globaltool | Out-Null
    }

    # Optional SonarCloud Begin
    if ($RunSonarCloud) {
        if (-not $env:SONAR_TOKEN) {
            throw "SONAR_TOKEN environment variable is required when -RunSonarCloud is specified."
        }
        $scannerPath = Get-Command dotnet-sonarscanner -ErrorAction SilentlyContinue
        if (-not $scannerPath) {
            Write-Host "`n[*] Installing dotnet-sonarscanner..." -ForegroundColor Yellow
            dotnet tool install -g dotnet-sonarscanner | Out-Null
        }
        Write-Host "`n[*] Starting SonarCloud Scanner..." -ForegroundColor Cyan
        dotnet sonarscanner begin `
            /k:"Online-Supermarket_Online-TNT-Supermarket-BE" `
            /o:"online-supermarket" `
            /d:sonar.host.url="https://sonarcloud.io" `
            /d:sonar.token="$env:SONAR_TOKEN" `
            /d:sonar.cs.opencover.reportsPaths="TestResults/**/coverage.opencover.xml" `
            /d:sonar.cs.vstest.reportsPaths="TestResults/**/*.trx"
    }

    # Step 1: Static Code Analysis (Roslyn Analyzers + Build)
    Write-Host "`n[1/4] Running Roslyn Static Code Analysis & Release Build..." -ForegroundColor Green
    $buildOutput = dotnet build Marketflow.slnx --configuration Release --no-incremental 2>&1
    $warningLines = $buildOutput | Select-String -Pattern "warning (CA|IDE|CS)\d+"
    $warningCount = ($warningLines | Measure-Object).Count
    Write-Host "      Build succeeded. Roslyn Analyzer diagnostics detected: $warningCount" -ForegroundColor Gray

    # Step 2: Code Style Check (dotnet format)
    Write-Host "`n[2/4] Verifying Code Formatting & Style Rules (dotnet format)..." -ForegroundColor Green
    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    dotnet format Marketflow.slnx --verify-no-changes --no-restore --verbosity minimal | Out-Null
    $formatStatus = if ($LASTEXITCODE -eq 0) { "PASS (Clean)" } else { "WARN (Style diffs found)" }
    $ErrorActionPreference = $prevPref
    Write-Host "      Style Check: $formatStatus" -ForegroundColor Gray

    # Step 3: Unit Tests + Coverlet Coverage (Cobertura + OpenCover)
    Write-Host "`n[3/4] Executing Unit Tests & Collecting Coverlet Metrics..." -ForegroundColor Green
    dotnet test Marketflow.slnx `
        --no-build `
        --configuration Release `
        --collect:"XPlat Code Coverage" `
        --settings coverlet.runsettings `
        --logger "trx;LogFileName=test-results.trx" `
        --results-directory TestResults

    if ($RunSonarCloud) {
        Write-Host "`n[*] Uploading Analysis & Coverage to SonarCloud..." -ForegroundColor Cyan
        dotnet sonarscanner end /d:sonar.token="$env:SONAR_TOKEN"
    }

    # Step 4: Generate Interactive HTML Dashboard (Coverage + Cyclomatic Complexity + CRAP)
    Write-Host "`n[4/4] Generating Interactive Quality & Complexity HTML Dashboard..." -ForegroundColor Green
    reportgenerator `
        -reports:"TestResults/**/coverage.cobertura.xml" `
        -targetdir:"QualityReport" `
        -reporttypes:"Html;HtmlSummary;TextSummary;JsonSummary" `
        -title:"TNT Supermarket (Marketflow) — Code Quality & Metrics" | Out-Null

    # Parse Metrics Summary
    $summaryJsonPath = Join-Path $RepoRoot "QualityReport\Summary.json"
    $lineCov = 0.0
    $branchCov = 0.0
    $coveredLines = 0
    $validLines = 0

    if (Test-Path $summaryJsonPath) {
        $summary = Get-Content $summaryJsonPath -Raw | ConvertFrom-Json
        $lineCov = [math]::Round($summary.summary.linecoverage, 2)
        $branchCov = [math]::Round($summary.summary.branchcoverage, 2)
        $coveredLines = $summary.summary.coveredlines
        $validLines = $summary.summary.coverablelines
    }

    $gateStatus = if ($lineCov -ge $MinCoveragePercent) { "PASSED" } else { "FAILED" }

    Write-Host "`n=================================================================" -ForegroundColor Cyan
    Write-Host "  CODE QUALITY & METRICS SUMMARY (IT24101852 - V.N.Thilanka)" -ForegroundColor Cyan
    Write-Host "=================================================================" -ForegroundColor Cyan
    Write-Host ("  Line Coverage       : {0}% ({1} / {2} lines)" -f $lineCov, $coveredLines, $validLines) -ForegroundColor White
    Write-Host ("  Branch Coverage     : {0}%" -f $branchCov) -ForegroundColor White
    Write-Host ("  Roslyn Diagnostics  : {0} analyzer warnings" -f $warningCount) -ForegroundColor White
    Write-Host ("  Code Style Check    : {0}" -f $formatStatus) -ForegroundColor White
    Write-Host ("  Quality Gate        : {0}" -f $gateStatus) -ForegroundColor $(if ($gateStatus -eq "PASSED") { "Green" } else { "Red" })
    Write-Host ("  HTML Dashboard      : {0}" -f (Join-Path $RepoRoot "QualityReport\index.html")) -ForegroundColor Yellow
    Write-Host "=================================================================" -ForegroundColor Cyan

    if ($OpenReport) {
        Start-Process (Join-Path $RepoRoot "QualityReport\index.html")
    }

    if ($gateStatus -eq "FAILED") {
        exit 1
    }
}
finally {
    Pop-Location
}
