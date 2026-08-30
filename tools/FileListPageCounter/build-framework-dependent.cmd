@echo off
setlocal
rem ---------------------------------------------------------------------------
rem  Smaller EXE (a few MB) that requires the .NET 8 Desktop Runtime on the
rem  target machine. Use build.cmd instead if the machine has no .NET installed.
rem ---------------------------------------------------------------------------

cd /d "%~dp0"

dotnet publish src\FileListPageCounter.App\FileListPageCounter.App.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained false ^
    -p:PublishSingleFile=true ^
    -p:DebugType=none ^
    -o publish-light || goto :failed

echo.
echo Done: publish-light\FileListPageCounter.exe
echo Requires the .NET 8 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/8.0
echo.
pause
exit /b 0

:failed
echo.
echo [!] Build failed. See the messages above.
echo.
pause
exit /b 1
