@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem  app-usage-tracker - 一键 CI 流水线
rem  clean -> build Release -> test Release -> publish self
rem ============================================================

rem 关键：禁用常驻 MSBuild / Roslyn 构建服务器。
rem 否则同一会话里 build/test 启动的常驻构建服务器会缓存"非单文件"构建求值，
rem 紧接着的 publish 复用该缓存、静默跳过单文件打包目标 GenerateBundle，
rem 导致最终产物从单个 EXE 退化成上百个散落 DLL。
set "DOTNET_CLI_USE_MSBUILD_SERVER=0"
set "DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1"

pushd "%~dp0"

call "%~dp0clean.bat"            || goto :Fail
call "%~dp0build.bat"   Release  || goto :Fail
call "%~dp0test.bat"    Release  || goto :Fail
call "%~dp0publish.bat" self     || goto :Fail

echo.
echo ============================================================
echo  [SUCCESS] CI pipeline completed.
echo  Artifact: %CD%\dist\AppUsageTracker.exe
echo ============================================================
popd
exit /b 0

:Fail
echo.
echo [FAILED] CI pipeline aborted.
popd
exit /b 1
