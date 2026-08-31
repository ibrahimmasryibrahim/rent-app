@echo off
setlocal
rem ---------------------------------------------------------------------------
rem  FILE LIST & PAGE COUNTER — build a standalone Windows EXE.
rem
rem  Double-click this file, or run it from a command prompt.
rem  Output: publish\FileListPageCounter.exe  (self-contained, portable)
rem
rem  Requires the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
rem ---------------------------------------------------------------------------

cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo [!] .NET SDK was not found.
    echo     Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
    echo     then run this file again.
    echo.
    pause
    exit /b 1
)

echo.
echo === Restoring packages =====================================================
dotnet restore FileListPageCounter.sln || goto :failed

echo.
echo === Running tests ==========================================================
dotnet test tests\FileListPageCounter.Tests\FileListPageCounter.Tests.csproj -c Release || goto :failed

echo.
echo === Publishing a self-contained single-file EXE =============================
dotnet publish src\FileListPageCounter.App\FileListPageCounter.App.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=none ^
    -o publish || goto :failed

echo.
echo === Done ===================================================================
echo   publish\FileListPageCounter.exe
echo   Copy the publish folder anywhere; no installation is required.
echo.
pause
exit /b 0

:failed
echo.
echo [!] Build failed. See the messages above.
echo.
pause
exit /b 1
