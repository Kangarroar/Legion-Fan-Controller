@echo off
setlocal

:: Check for administrative privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERROR: This script must be run as Administrator.
    echo Please right-click and "Run as administrator".
    pause
    exit /b
)

:: Get the path to the DLL relative to this script
set "DLL_PATH=%~dp0Fan Control\WinRing0x64.dll"

echo.
echo Adding Windows Defender exclusion for:
echo "%DLL_PATH%"
echo.

:: Execute the PowerShell command to add the exclusion
powershell -Command "Add-MpPreference -ExclusionPath '%DLL_PATH%'"

if %errorLevel% == 0 (
    echo.
    echo SUCCESS: Permanent exclusion added to Windows Defender.
    echo.
) else (
    echo.
    echo FAILED: Could not add exclusion. 
    echo Ensure Windows Defender is active and you have permission.
    echo.
)

pause
