@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem  AppUsageTracker - 打包脚本（dev + release）
rem  用法: package.bat
rem  等价命令: sh manager.sh package
rem
rem  产物（dist\ 已 gitignore）:
rem    dist\dev\AppUsageTracker_<版本>_x64-unpack-debug.zip
rem    dist\release\unpack\                              （可直接运行）
rem    dist\release\AppUsageTracker_<版本>_x64-unpack.zip（zip 内无顶层目录）
rem
rem  说明:
rem    - 版本号单一来源: 主项目 AppUsageTracker.csproj 的 <Version>。
rem    - 不生成 setup.exe: release 以免安装解压包分发。
rem    - 检测到运行中的 AppUsageTracker 时会先列出进程并询问：输入 Y 才关闭
rem      （先尝试正常关闭，超时再强制结束）并继续打包；其它输入取消。
rem      不会自动关闭任何实例。
rem    - 本脚本不暂停，便于 manager.sh / ci.bat 自动化调用；失败时返回非 0。
rem ============================================================

pushd "%~dp0"

where powershell >nul 2>&1
if errorlevel 1 goto :NoPowerShell

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package.ps1" %*
set "EXITCODE=%ERRORLEVEL%"
popd
exit /b %EXITCODE%

:NoPowerShell
echo [ERROR] 未找到 powershell.exe。
popd
exit /b 1
