@echo off
rem DanCI - Metre ^& BOQ : installation pour l utilisateur courant (double-cliquer).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer.ps1" %*
echo.
pause
