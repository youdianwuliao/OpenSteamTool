@echo off

setlocal EnableDelayedExpansion
cd /d "%~dp0"

REM ---------------------------------------------------------------------------
REM  打包完整 Release
REM    1) 构建主 DLL（Release/Debug + extract_tickets）
REM    2) 发布启动器与票据探针（self-contained 单文件）
REM    3) 组装 dist\OpenSteamTool-<version>\ 并压缩成 zip
REM
REM  用法：package-release.bat [version]
REM        不传版本号时使用 git describe。
REM ---------------------------------------------------------------------------

set "VERSION=%~1"
if "%VERSION%"=="" (
    for /f "delims=" %%v in ('git describe --tags --always --dirty 2^>nul') do set "VERSION=%%v"
)
if "%VERSION%"=="" set "VERSION=dev"

echo [INFO] 版本: %VERSION%

echo [INFO] 构建主 DLL...
call build.bat
if errorlevel 1 goto :fail

set "HAVE_DOTNET=1"
where dotnet >nul 2>nul
if errorlevel 1 set "HAVE_DOTNET=0"

if "%HAVE_DOTNET%"=="0" (
    echo [WARN] 未找到 .NET 8 SDK，跳过启动器/探针打包
) else (
    echo [INFO] 发布启动器...
    dotnet publish "launcher\OpenSteamTool.Launcher\OpenSteamTool.Launcher.csproj" ^
        -c Release -r win-x64 --self-contained true ^
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
        -p:EnableCompressionInSingleFile=true -o "build\launcher"
    if errorlevel 1 goto :fail

    echo [INFO] 发布票据探针...
    dotnet publish "launcher\OpenSteamTool.Probe\ost-ticket-probe.csproj" ^
        -c Release -r win-x64 --self-contained true ^
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
        -o "build\launcher"
    if errorlevel 1 goto :fail
)

set "STAGE=build\dist\OpenSteamTool-%VERSION%"
if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%" 2>nul

copy /y "build\Release\OpenSteamTool.dll" "%STAGE%\" >nul
copy /y "build\Release\dwmapi.dll" "%STAGE%\" >nul
copy /y "build\Release\xinput1_4.dll" "%STAGE%\" >nul
copy /y "opensteamtool.example.toml" "%STAGE%\" >nul

if exist "build\launcher\OpenSteamTool.Launcher.exe" (
    mkdir "%STAGE%\launcher" 2>nul
    copy /y "build\launcher\OpenSteamTool.Launcher.exe" "%STAGE%\launcher\" >nul
    copy /y "build\launcher\ost-ticket-probe.exe" "%STAGE%\launcher\" >nul
    copy /y "launcher\README.md" "%STAGE%\launcher\" >nul
)

set "ZIP=build\dist\OpenSteamTool-%VERSION%.zip"
if exist "%ZIP%" del /q "%ZIP%"

echo [INFO] 压缩...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 goto :fail

echo [OK] 输出: %ZIP%
exit /b 0

:fail
echo [ERROR] 打包失败
exit /b 1
