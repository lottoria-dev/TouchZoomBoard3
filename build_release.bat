@echo off
setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_release.ps1" -Version "3.0.8"
if errorlevel 1 (
    echo.
    echo [ERROR] Failed to create the release package.
    pause
    exit /b 1
)

echo.
echo TouchZoomBoard3 3.0.8 package created.
pause
