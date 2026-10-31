@echo off
rem Rebuild Debug and start the app for development. Existing instances are asked to close normally.
call "%~dp0_ps.bat" dev %*
exit /b %ERRORLEVEL%
