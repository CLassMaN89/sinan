@echo off
chcp 65001 > nul
cls

echo.
echo ========================================================
echo   PACS CD TRANSFER - Otomatik Kurulum ve Baslatma
echo ========================================================
echo.

REM Check if exe exists
if not exist "PacsCdTransfer.Web.exe" (
    echo HATA: PacsCdTransfer.Web.exe bulunamadi!
    pause
    exit /b 1
)

REM Fix appsettings.json - change port
echo 1/3 Ayarlar duzenliyor...
powershell -Command "(Get-Content 'appsettings.json') -replace '5062', '5000' | Set-Content 'appsettings.json'"

REM Fix app.html - add charset
echo 2/3 Karakter kodlamasi duzenliyor...
powershell -Command "
$content = Get-Content 'wwwroot/app.html' -Raw
if ($content -notmatch '<meta charset') {
    $content = $content -replace '<title>', '<meta charset=`"UTF-8`">`r`n<title>'
    $content | Set-Content 'wwwroot/app.html'
}
"

echo 3/3 Uygulama baslatiliyor...
echo.
echo Uygulama http://localhost:5000 adresinde calisacak
echo.

REM Open browser
timeout /t 1 /nobreak
start http://localhost:5000

REM Run app
PacsCdTransfer.Web.exe

pause
