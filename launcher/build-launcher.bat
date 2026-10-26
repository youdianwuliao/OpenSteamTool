@echo off

setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] 需要 .NET 8 SDK: https://dotnet.microsoft.com/download
    exit /b 1
)

echo [INFO] 发布启动器（self-contained 单文件）...
dotnet publish "OpenSteamTool.Launcher\OpenSteamTool.Launcher.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o "..\build\launcher"
if errorlevel 1 goto :fail

echo [INFO] 发布票据探针...
dotnet publish "OpenSteamTool.Probe\ost-ticket-probe.csproj" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "..\build\launcher"
if errorlevel 1 goto :fail

echo [OK] 输出目录: %~dp0..\build\launcher
echo       OpenSteamTool.Launcher.exe / ost-ticket-probe.exe
exit /b 0

:fail
echo [ERROR] 发布失败
exit /b 1
