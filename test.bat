@echo off
rem Test entry. Usage: test.bat [Debug|Release] [additional dotnet test arguments]
call "%~dp0_ps.bat" test %*
exit /b %ERRORLEVEL%
