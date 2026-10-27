@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "SCRIPT_DIR=%CD%"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%\PublishCleanSource.ps1"
set "EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%EXIT_CODE%"=="0" echo PUBLISH FAILED. Send a screenshot of the full output. Do not force-push.
pause
exit /b %EXIT_CODE%
