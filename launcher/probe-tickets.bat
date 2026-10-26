@echo off

setlocal
cd /d "%~dp0"

if "%~1"=="" (
    echo 用法: probe-tickets.bat ^<AppId^> [SteamRoot]
    echo 示例: probe-tickets.bat 1361510
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] 需要 .NET 8 SDK: https://dotnet.microsoft.com/download
    exit /b 1
)

dotnet run --project "OpenSteamTool.Probe\ost-ticket-probe.csproj" -c Release -- %*
exit /b %errorlevel%
