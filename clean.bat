@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion

rem ============================================================
rem  app-usage-tracker - 清理脚本
rem  清理 src/ 与 tests/ 下的 bin/obj，以及发布产物 dist/
rem ============================================================

pushd "%~dp0"

echo ============================================================
echo  Clean  ^|  移除 bin / obj / dist
echo ============================================================

rem 先结束可能驻留后台的程序实例（本程序支持托盘常驻），
rem 否则其占用的 dist\AppUsageTracker.exe 等文件无法被删除。
taskkill /IM AppUsageTracker.exe /F >nul 2>&1

call :CleanProject "src\AppUsageTracker"
call :CleanProject "tests\AppUsageTracker.Tests"
call :CleanDist

echo.
echo [SUCCESS] 清理完成。
popd
exit /b 0

:CleanProject
set "P=%~1"
if exist "%P%\bin" ( echo  - %P%\bin     & rmdir /S /Q "%P%\bin"     )
if exist "%P%\obj" ( echo  - %P%\obj     & rmdir /S /Q "%P%\obj"     )
exit /b 0

:CleanDist
if exist "dist" ( echo  - dist & rmdir /S /Q "dist" )
exit /b 0
