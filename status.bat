@echo off
rem Show app process and script-managed run state.
call "%~dp0_ps.bat" desktop status %*
exit /b %ERRORLEVEL%
