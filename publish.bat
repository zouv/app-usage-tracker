@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem  兼容旧入口: publish.bat 现在等价于 package.bat。
rem  旧的 self / fx 参数不再生效；release 固定为 win-x64 自包含单文件。
rem  需要追加 dotnet publish 参数时请直接使用 package.bat。
rem ============================================================

if not "%~1"=="" echo [WARN] publish.bat 参数已废弃并忽略：%*

call "%~dp0package.bat"
set "EXITCODE=%ERRORLEVEL%"
exit /b %EXITCODE%
