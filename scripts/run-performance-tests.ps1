<#
.SYNOPSIS
    Runs Apache JMeter Performance and Load Testing against TNT Supermarket (Marketflow) Microservices.

.DESCRIPTION
    Executes automated load and stress testing using Apache JMeter in Non-GUI (headless) mode.
    Tests API Gateway and microservice endpoints under concurrent user workloads (default: 110 concurrent users).
    Generates JTL raw logs and an interactive HTML Dashboard report in jmeter_html_report.

.PARAMETER Users
    Number of concurrent virtual users (threads). Default is 110.

.PARAMETER RampUp
    Ramp-up period in seconds. Default is 10 seconds.

.PARAMETER Loops
    Number of loop iterations per user. Default is 5 iterations.

.PARAMETER HostName
    Target host (default: localhost).

.PARAMETER Port
    Target gateway port (default: 8080).

.PARAMETER OpenReport
    Automatically open the generated interactive HTML report in default browser.

.EXAMPLE
    .\scripts\run-performance-tests.ps1
    .\scripts\run-performance-tests.ps1 -Users 110 -RampUp 10 -OpenReport
    .\scripts\run-performance-tests.ps1 -Users 200 -RampUp 15 -OpenReport
#>

param(
    [int]$Users = 110,
    [int]$RampUp = 10,
    [int]$Loops = 5,
    [string]$HostName = "localhost",
    [int]$Port = 8080,
    [switch]$OpenReport
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$JmxPath = Join-Path $RepoRoot "TNT_Supermarket_Performance_Test.jmx"
$JtlPath = Join-Path $RepoRoot "jmeter_results.jtl"
$ReportDir = Join-Path $RepoRoot "jmeter_html_report"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   TNT Supermarket - Performance & Load Testing Pipeline  " -ForegroundColor Cyan
Write-Host "   Target: $Users Concurrent Users (Configurable)         " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Locate JMeter Executable or ApacheJMeter.jar
$jmeterJar = $null
$jmeterBat = $null

$knownJar = "$env:USERPROFILE\Downloads\apache-jmeter-5.6.3\bin\ApacheJMeter.jar"
if (Test-Path $knownJar) {
    $jmeterJar = $knownJar
}

$knownBat = "$env:USERPROFILE\Downloads\apache-jmeter-5.6.3\bin\jmeter.bat"
if (Test-Path $knownBat) {
    $jmeterBat = $knownBat
}

if (-not $jmeterJar -and -not $jmeterBat) {
    $cmdInPath = Get-Command "jmeter" -ErrorAction SilentlyContinue
    if ($cmdInPath) {
        $jmeterBat = $cmdInPath.Source
    }
}

if (-not $jmeterJar -and -not $jmeterBat) {
    Write-Error "Apache JMeter not found! Please ensure JMeter is placed in Downloads\apache-jmeter-5.6.3."
    exit 1
}

if ($jmeterJar) {
    Write-Host "[OK] Using ApacheJMeter.jar directly at: $jmeterJar" -ForegroundColor Green
} else {
    Write-Host "[OK] Using JMeter executable at: $jmeterBat" -ForegroundColor Green
}

Write-Host "[CONFIG] Virtual Users: $Users | Ramp-up: ${RampUp}s | Loops: $Loops | Gateway: ${HostName}:${Port}" -ForegroundColor Yellow

# 2. Clean previous test results and reports
if (Test-Path $JtlPath) {
    Write-Host "[CLEANUP] Removing previous results log: $JtlPath" -ForegroundColor DarkGray
    Remove-Item -Force $JtlPath
}
if (Test-Path $ReportDir) {
    Write-Host "[CLEANUP] Removing previous HTML report: $ReportDir" -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $ReportDir
}

# 3. Execute JMeter in Non-GUI mode directly
Write-Host "`n>>> Running Performance Test Plan: $JmxPath <<<" -ForegroundColor Green

if ($jmeterJar) {
    java -jar $jmeterJar -n -t $JmxPath -l $JtlPath -e -o $ReportDir "-JHOST=$HostName" "-JPORT=$Port" "-JUSERS=$Users" "-JRAMPUP=$RampUp" "-JLOOPS=$Loops"
} else {
    & $jmeterBat -n -t $JmxPath -l $JtlPath -e -o $ReportDir "-JHOST=$HostName" "-JPORT=$Port" "-JUSERS=$Users" "-JRAMPUP=$RampUp" "-JLOOPS=$Loops"
}

# 4. Check generated report
$indexHtml = Join-Path $ReportDir "index.html"
if (Test-Path $indexHtml) {
    Write-Host "`n==========================================================" -ForegroundColor Green
    Write-Host " [SUCCESS] Performance & Load Test Completed Successfully! " -ForegroundColor Green
    Write-Host " Report generated at: $indexHtml" -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Green

    if ($OpenReport) {
        Write-Host "[INFO] Opening HTML Dashboard in default browser..." -ForegroundColor Yellow
        Start-Process $indexHtml
    }
} else {
    Write-Warning "HTML report was not generated. Check $RepoRoot\jmeter.log for details."
}
