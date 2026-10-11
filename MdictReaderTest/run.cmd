@echo off
cd /d "%~dp0"
set "DOTNET=dotnet"
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    "%USERPROFILE%\.dotnet\dotnet.exe" --version >nul 2>&1
    if not errorlevel 1 set "DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
)
"%DOTNET%" run --project "MdictReaderTest.csproj" -- %*
set "RESULT=%ERRORLEVEL%"
if errorlevel 1 pause
exit /b %RESULT%
