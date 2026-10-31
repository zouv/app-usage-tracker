@echo off
rem Remove build artifacts. Refuses to clean while the app is running.
call "%~dp0_ps.bat" clean %*
exit /b %ERRORLEVEL%
