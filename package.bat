@echo off
rem Alias for publish.bat, matching the manager.sh pack terminology.
call "%~dp0_ps.bat" package %*
exit /b %ERRORLEVEL%
