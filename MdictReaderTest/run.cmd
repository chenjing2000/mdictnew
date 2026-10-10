@echo off
cd /d "%~dp0"
dotnet run --project "MdictReaderTest.csproj" -- %*
if errorlevel 1 pause
