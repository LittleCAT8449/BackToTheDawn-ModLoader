@echo off
setlocal
cd /d "%~dp0"

if not exist "Back To The Dawn.exe" (
    echo [ERROR] Back To The Dawn.exe was not found.
    pause
    exit /b 1
)

if not exist "winhttp.dll" (
    echo [ERROR] BepInEx is not installed. Run scripts\Install-BepInEx.ps1 first.
    pause
    exit /b 1
)

echo Starting Back To The Dawn with BepInEx...
start "" "Back To The Dawn.exe"
endlocal

