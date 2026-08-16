@echo off
chcp 65001 > nul

echo.
echo ========================================================
echo   PACS CD TRANSFER - Starting Application...
echo ========================================================
echo.

if not exist "PacsCdTransfer.Web.exe" (
    echo ERROR: PacsCdTransfer.Web.exe not found!
    echo.
    echo All files must be in the same directory.
    pause
    exit /b 1
)

echo Starting application on http://localhost:5062
echo.
start http://localhost:5062
timeout /t 2 /nobreak
PacsCdTransfer.Web.exe

pause
