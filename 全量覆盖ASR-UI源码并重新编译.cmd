@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "UPDATE_DIR=%CD%"
set "TARGET=%USERPROFILE%\Desktop\ASR-UI"

echo.
echo ================================================================
echo  Apply complete ASR-UI v0.1.4 source and rebuild on Actions
echo ================================================================
echo Update folder: %UPDATE_DIR%
echo Target folder: %TARGET%
echo.

where robocopy >nul 2>nul
if errorlevel 1 goto :no_robocopy
if not exist "%TARGET%" goto :no_target

echo [1/2] Copying complete clean source tree...
robocopy "%UPDATE_DIR%" "%TARGET%" /E /R:1 /W:1 /XD .git
set "COPY_EXIT=%ERRORLEVEL%"
echo Robocopy exit code: %COPY_EXIT%
if %COPY_EXIT% GEQ 8 goto :copy_failed
if not exist "%TARGET%\scripts\PublishCleanSource.ps1" goto :no_publish_script

echo [2/2] Rebuilding clean Git source change set and pushing main...
cd /d "%TARGET%"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%TARGET%\scripts\PublishCleanSource.ps1"
set "PUSH_EXIT=%ERRORLEVEL%"
if not "%PUSH_EXIT%"=="0" goto :push_failed

echo.
echo SUCCESS. GitHub Actions will now rebuild ASR-UI v0.1.4 from source.
pause
exit /b 0

:no_robocopy
echo ERROR: Robocopy was not found.
pause
exit /b 1

:no_target
echo ERROR: Target source folder was not found.
echo Expected: %TARGET%
pause
exit /b 1

:no_publish_script
echo ERROR: PublishCleanSource.ps1 was not copied to the target.
pause
exit /b 1

:copy_failed
echo COPY FAILED. Send a screenshot of the output above.
pause
exit /b 1

:push_failed
echo PUSH FAILED. No force-push was performed. Send a screenshot.
pause
exit /b 1
