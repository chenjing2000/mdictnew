@echo off
cd /d "%~dp0"
dotnet run --project "MdictReaderTest.csproj" -- --self-test
if errorlevel 1 pause
