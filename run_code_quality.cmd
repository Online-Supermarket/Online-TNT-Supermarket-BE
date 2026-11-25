@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0scripts\run-code-quality.ps1" -OpenReport
