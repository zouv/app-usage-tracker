@echo off
rem Publish a verified single-file exe. Usage: publish.bat [self|fx]
call "%~dp0_ps.bat" package %*
exit /b %ERRORLEVEL%
