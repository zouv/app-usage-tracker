@echo off
chcp 65001 >nul

rem ============================================================
rem  app-usage-tracker - 发布单文件 EXE
rem  用法:
rem    publish.bat            默认 self（自包含）
rem    publish.bat self       自包含（打包 .NET 运行时）
rem    publish.bat fx         依赖框架（需已装 .NET 8 桌面运行时）
rem ============================================================

set "MODE=%~1"
if "%MODE%"=="" set "MODE=self"

rem 使用绝对路径定位项目；脚本执行时切到项目根目录。
rem 注意：不要用 OUTDIR 作为变量名——它是 MSBuild 保留属性 OutDir，
rem 会被环境变量覆盖编译输出目录，导致单文件打包回退成散落 DLL。
set "ROOT=%~dp0"
set "PROJ=%ROOT%src\AppUsageTracker\AppUsageTracker.csproj"
set "PROJDIR=%ROOT%src\AppUsageTracker"
set "TESTDIR=%ROOT%tests\AppUsageTracker.Tests"
set "DIST_DIR=%ROOT%dist"
set "RID=win-x64"

if /I "%MODE%"=="self" goto :ModeSelf
if /I "%MODE%"=="fx" goto :ModeFx
echo [ERROR] 未知模式：%MODE% 。请使用 self 或 fx。
exit /b 1

:ModeSelf
set "SELFFLAG=true"
set "MODE_DESC=自包含"
goto :ModeDone

:ModeFx
set "SELFFLAG=false"
set "MODE_DESC=依赖框架"
goto :ModeDone

:ModeDone
echo ============================================================
echo  Publish ^|  Mode    = %MODE_DESC%
echo  Project : %PROJ%
echo  Output  : %DIST_DIR%
echo  RID     : %RID%
echo ============================================================

where dotnet >nul 2>&1
if errorlevel 1 goto :NoDotnet

rem 结束可能驻留后台的程序实例（本程序支持托盘常驻），
rem 否则其占用的 dist\AppUsageTracker.exe 会令单文件打包任务 GenerateBundle
rem 因无法覆盖而失败（MSB4018），回退成散落 DLL。
echo Stopping any running AppUsageTracker instances...
taskkill /IM AppUsageTracker.exe /F >nul 2>&1

rem 发布前彻底清空 bin/obj/dist。
rem 若直接基于 build/test 的普通构建产物增量 publish，会静默跳过单文件
rem 打包目标（GenerateBundle 不运行，无报错），导致输出散落上百个 DLL。
echo Cleaning bin/obj/dist to force a clean single-file publish...
if exist "%DIST_DIR%"      rmdir /S /Q "%DIST_DIR%"
if exist "%PROJDIR%\bin"   rmdir /S /Q "%PROJDIR%\bin"
if exist "%PROJDIR%\obj"   rmdir /S /Q "%PROJDIR%\obj"
if exist "%TESTDIR%\bin"   rmdir /S /Q "%TESTDIR%\bin"
if exist "%TESTDIR%\obj"   rmdir /S /Q "%TESTDIR%\obj"

cd /d "%ROOT%"

rem 注意：
rem - 单文件打包所需的 IncludeNativeLibrariesForSelfExtract / DebugType 已由
rem   csproj 配置，不要在命令行重复传入，否则可能与单文件打包目标冲突导致回退。
rem - 显式关闭 PublishReadyToRun：本机 SDK 的 R2R 与 WPF+WinForms 单文件
rem   发布组合不稳定，会间歇性触发 NETSDK1096 crossgen 失败。
rem - -o 使用相对路径 dist：本机 dotnet CLI 对"cmd 变量展开的绝对路径"
rem   解析有缺陷，会静默跳过单文件打包而输出散落 DLL；相对路径最稳妥。
rem - 脚本保持无 EnableDelayedExpansion、无括号块，避免 cmd 解析器干扰 MSBuild。
dotnet publish "%PROJ%" -c Release -r %RID% --self-contained %SELFFLAG% -p:PublishSingleFile=true -p:PublishReadyToRun=false -o dist --nologo

if errorlevel 1 goto :PublishFailed

echo.
echo ============================================================
echo  Output files:
echo ============================================================
rem 单文件发布成功时此处应只列出 1 个 exe；若出现大量 DLL，说明单文件打包被回退。
rem 计数和列表用纯 for 循环，不用 find（cmd 环境下 find 可能被 Git Bash
rem 的 Unix find 抢占，遍历整个盘符而卡死）。
set "FILECOUNT=0"
for %%F in ("%DIST_DIR%\*") do set /a "FILECOUNT+=1"
if %FILECOUNT% GTR 5 goto :TooManyFiles

for %%F in ("%DIST_DIR%\*") do echo   %%~nxF   %%~zF bytes

echo.
echo [SUCCESS] 发布完成。
echo  Folder: %DIST_DIR%
exit /b 0

:NoDotnet
echo [ERROR] 未找到 dotnet CLI，请先安装 .NET 8 SDK。
exit /b 1

:PublishFailed
echo.
echo [FAILED] 发布失败。
exit /b 1

:TooManyFiles
echo.
echo [FAILED] 输出有 %FILECOUNT% 个文件 - 单文件打包回退成了散落布局。
echo          请确认没有 AppUsageTracker.exe 在运行后重试。
exit /b 1
