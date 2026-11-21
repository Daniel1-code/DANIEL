@echo off
rem DanCI - Metre ^& BOQ : desinstallation (double-cliquer).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer.ps1" -Uninstall
echo.
pause
