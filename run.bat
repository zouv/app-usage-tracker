@echo off
rem Start the built app; build first when needed. Usage: run.bat [Debug|Release] [app arguments]
call "%~dp0_ps.bat" desktop start %*
exit /b %ERRORLEVEL%
