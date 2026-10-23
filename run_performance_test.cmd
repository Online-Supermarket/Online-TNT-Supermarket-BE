@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-performance-tests.ps1 -Users 110 -RampUp 10 -Loops 5 -OpenReport
pause
