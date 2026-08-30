@echo off
setlocal
cd /d "%~dp0"
dotnet test FileListPageCounter.sln -c Release
pause
