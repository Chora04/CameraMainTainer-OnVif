@echo off
title CameraMaintainer Web
echo.
echo ============================================
echo   CameraMaintainer Web Server
echo ============================================
echo.
cd /d "%~dp0"

if exist "publish\CameraMaintainer.Web.exe" (
    echo Starting published release...
    echo.
    cd /d "%~dp0publish"
    CameraMaintainer.Web.exe
) else (
    echo Published release not found. Starting from source...
    echo.
    dotnet run --project src\CameraMaintainer.Web -c Release
)

pause
