@echo off
rem Ask the app to close normally and wait. This script never force-kills the process.
call "%~dp0_ps.bat" desktop stop %*
exit /b %ERRORLEVEL%
