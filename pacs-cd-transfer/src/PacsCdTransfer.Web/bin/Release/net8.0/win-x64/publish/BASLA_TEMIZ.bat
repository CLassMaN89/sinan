@echo off
cls
echo.
echo PACS CD TRANSFER - Baslatiliyor...
echo.

if not exist "PacsCdTransfer.Web.exe" (
    echo HATA: PacsCdTransfer.Web.exe bulunamadi!
    pause
    exit /b 1
)

REM Kill any existing instance
echo Onceki ornekleri kapatiliyor...
taskkill /IM PacsCdTransfer.Web.exe /F /T >nul 2>&1

REM Wait a moment for port to be released
timeout /t 2 /nobreak

REM Set environment variable for port
set ASPNETCORE_URLS=http://localhost:5000

echo.
echo Baslatiliyor: http://localhost:5000
echo.

timeout /t 2
start http://localhost:5000

PacsCdTransfer.Web.exe

pause
