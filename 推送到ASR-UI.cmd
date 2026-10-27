@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "ROOT=%CD%"
set "REPO_URL=https://github.com/xzlosats111/ASR-UI.git"
set "PROXY_URL=http://127.0.0.1:10809"

echo.
echo ================================================================
echo  Push ASR-UI source to GitHub
echo ================================================================
echo Source folder: %ROOT%
echo Remote: %REPO_URL%
echo Proxy: %PROXY_URL%
echo.

where git >nul 2>nul
if errorlevel 1 goto :no_git
if not exist "%ROOT%\.git" (
  echo Initializing a new local Git repository...
  git init
  if errorlevel 1 goto :failed
)

echo [1/4] Configuring proxy and remote...
git config http.proxy "%PROXY_URL%"
git config http.version HTTP/1.1
git config http.postBuffer 524288000
git config http.lowSpeedLimit 0
git config http.lowSpeedTime 999999
git remote remove origin >nul 2>nul
git remote add origin "%REPO_URL%"
if errorlevel 1 goto :failed

git config user.name >nul 2>nul
if errorlevel 1 goto :no_identity
git config user.email >nul 2>nul
if errorlevel 1 goto :no_identity

echo [2/4] Staging source files...
git add -A
if errorlevel 1 goto :failed

echo [3/4] Creating commit when needed...
git diff --cached --quiet
if errorlevel 1 goto :commit
echo No source changes to commit.
goto :push

:commit
git commit -m "Initial ASR-UI workbench with Qwen and Parakeet"
if errorlevel 1 goto :failed

:push
echo [4/4] Pushing main to GitHub...
git branch -M main
git push -u origin main
if errorlevel 1 goto :push_failed

echo.
echo SUCCESS. Open GitHub Actions and wait for Build ASR-UI for Windows.
pause
exit /b 0

:no_git
echo ERROR: Git for Windows was not found.
pause
exit /b 1

:no_identity
echo ERROR: Git author name/email is not configured.
echo Run these once, then retry:
echo   git config --global user.name "Your Name"
echo   git config --global user.email "you@example.com"
pause
exit /b 1

:push_failed
echo PUSH FAILED. Send a screenshot of the full error above.
pause
exit /b 1

:failed
echo UPDATE FAILED. Send a screenshot of the full error above.
pause
exit /b 1
