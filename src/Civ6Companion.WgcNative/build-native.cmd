@echo off
setlocal
PowerShell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-native.ps1" -Configuration "%~1"
exit /b %ERRORLEVEL%
