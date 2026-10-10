@echo off
cd /d "%~dp0"
dotnet run --project "Mdict.Wpf.csproj" -- %*
if errorlevel 1 pause
