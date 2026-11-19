@echo off
rem Rebuild Debug and start the app for development. A running instance is force-closed first.
call "%~dp0_ps.bat" dev %*
exit /b %ERRORLEVEL%
