@echo off
chcp 65001 >nul
rem Shared BAT entry. Set APP_USAGE_TRACKER_NO_PAUSE=1 for CI / automation.
setlocal
cd /d "%~dp0"
set "SCRIPT=%~1"
if "%SCRIPT%"=="" (
    echo [ERROR] Missing PowerShell script name.
    exit /b 2
)
shift
set "ARGS="
:collect
if "%~1"=="" goto run
set ARGS=%ARGS% %1
shift
goto collect
:run
where pwsh >nul 2>nul
if errorlevel 1 (set "PS=powershell") else (set "PS=pwsh")
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\%SCRIPT%.ps1"%ARGS%
set "RC=%ERRORLEVEL%"
echo.
if "%RC%"=="0" (echo [DONE]) else (echo [FAILED] Exit code %RC%)
if not "%APP_USAGE_TRACKER_NO_PAUSE%"=="1" pause
exit /b %RC%
