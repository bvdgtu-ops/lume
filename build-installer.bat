@echo off
rem Сборка одного самодостаточного установщика.
rem Живёт в проекте; исходники установщика и готовый exe — в соседней папке "..\Lume Installer".
rem Промежуточные файлы пишутся во временную папку и удаляются, проект остаётся чистым.
setlocal
cd /d "%~dp0"
set "SETUP=..\Lume Installer"
set "TMPUB=%TEMP%\lume_build"

echo [1/4] Publishing Lume (self-contained)...
dotnet publish Lume\Lume.csproj -c Release -r win-x64 --self-contained true -o "%TMPUB%\app"
if errorlevel 1 goto :err

echo [2/4] Building uninstaller (single file)...
dotnet publish Uninstaller\Uninstaller.csproj -c Release
if errorlevel 1 goto :err

echo [3/4] Assembling payload...
if exist "%SETUP%\Installer\payload" rd /s /q "%SETUP%\Installer\payload"
xcopy "%TMPUB%\app\*" "%SETUP%\Installer\payload\" /e /i /y >nul
copy /y "Uninstaller\Release\win-x64\publish\Uninstaller.exe" "%SETUP%\Installer\payload\Uninstall Lume.exe" >nul

echo [4/4] Building single-file setup (packing payload, be patient)...
rem ВАЖНО: publish без -o! С -o SDK выдаёт битый single-file (краш 0xC000041D при старте)
dotnet publish "%SETUP%\Installer\Installer.csproj" -c Release
if errorlevel 1 goto :err
copy /y "%SETUP%\Installer\Release\win-x64\publish\LumeSetup.exe" "%SETUP%\LumeSetup.exe" >nul

rd /s /q "%TMPUB%" >nul 2>&1
rd /s /q "%SETUP%\Installer\payload" >nul 2>&1
rd /s /q "%SETUP%\Installer\obj" >nul 2>&1
rd /s /q "%SETUP%\Installer\Release" >nul 2>&1
rd /s /q Lume\obj >nul 2>&1

rem Uninstaller\Release оставляем только с одним однофайловым exe
if exist "Uninstaller\Release\win-x64\publish\Uninstaller.exe" (
  move /y "Uninstaller\Release\win-x64\publish\Uninstaller.exe" "Uninstaller\_single.exe" >nul
  rd /s /q Uninstaller\Release
  md Uninstaller\Release
  move /y "Uninstaller\_single.exe" "Uninstaller\Release\Uninstaller.exe" >nul
)

echo.
echo Done: "%SETUP%\LumeSetup.exe"
goto :eof

:err
echo BUILD FAILED
exit /b 1
