@echo off
rem Build entry. Usage: build.bat [Debug|Release] [additional dotnet build arguments]
call "%~dp0_ps.bat" build %*
exit /b %ERRORLEVEL%
