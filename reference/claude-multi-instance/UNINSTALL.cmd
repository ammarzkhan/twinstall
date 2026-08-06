@echo off
title Claude Multi-Instance - Uninstall
echo.
echo   This removes the router, icon watcher, launchers and registry entries.
echo   Your Claude profiles and sign-ins are kept unless you say otherwise.
echo.
set "RP="
set /p RP=  Also delete the second instance profile ^(loses its sign-in^)? [y/N]: 
if /i "%RP%"=="y" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-ClaudeMultiInstance.ps1" -Uninstall -RemoveProfiles
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-ClaudeMultiInstance.ps1" -Uninstall
)
echo.
pause
