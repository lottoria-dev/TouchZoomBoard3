@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_release.ps1" -Version "3.0.2"

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Failed to create the release package.
    pause
    exit /b 1
)

echo.
echo TouchZoomBoard3 3.0.2 package created.
pause