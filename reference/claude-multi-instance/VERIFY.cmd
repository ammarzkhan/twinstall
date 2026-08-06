@echo off
title Claude Multi-Instance - Verify
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-ClaudeMultiInstance.ps1" -Verify
echo.
pause
