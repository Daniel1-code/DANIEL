@echo off
rem DanCI - Metre ^& BOQ : composants PDF (double-cliquer, une seule fois).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer.ps1" %*
echo.
pause
