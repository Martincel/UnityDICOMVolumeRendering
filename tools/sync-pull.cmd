@echo off
rem Dohvaca najnovije promjene s GitHuba za trenutnu granu (samo fast-forward, nista se ne spaja samo od sebe).
rem Pokreni prije rada na ovom uredaju. Repozitorij se pronalazi prema lokaciji skripte (mapa iznad tools\).

cd /d "%~dp0.."
echo Repozitorij: %CD%
for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD') do set BRANCH=%%b
echo Grana:       %BRANCH%

git config --get branch.%BRANCH%.remote >nul 2>&1
if errorlevel 1 (
    echo.
    echo Grana %BRANCH% nema upstream. Jednom pokreni: git push -u origin %BRANCH%
    goto :kraj
)

tasklist /fi "imagename eq Unity.exe" 2>nul | find /i "Unity.exe" >nul
if not errorlevel 1 echo Upozorenje: Unity je otvoren. Zatvori ga prije pulla da ne ostane stari import.

git fetch origin
if errorlevel 1 (
    echo.
    echo Fetch nije uspio ^(mreza / prijava na GitHub^).
    goto :kraj
)

git pull --ff-only
if errorlevel 1 (
    echo.
    echo Pull nije moguc kao fast-forward ^(povijest se razisla ili lokalne izmjene smetaju^).
    echo Nista nije promijenjeno. Javi Claudeu ili rijesi rucno.
    goto :kraj
)

echo.
echo Gotovo. Zadnji commit:
git log -1 --format="%%h %%s (%%cr)"

:kraj
echo.
pause
