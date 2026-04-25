@echo off
setlocal enabledelayedexpansion

echo ################################################
echo #      Project Diablo 2 Standalone Builder     #
echo ################################################

:: Try to find C# compiler (Standard Windows path)
set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "!CSC_PATH!" (
    set CSC_PATH=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
)

if exist "!CSC_PATH!" (
    echo [BUILD] C# compiler found. Compiling standalone.cs...
    "!CSC_PATH!" /out:PD2_MapReveal.exe /optimize /platform:x86 standalone.cs
) else (
    echo [ERROR] No compiler found.
    pause
    exit /b
)

if %errorlevel% equ 0 (
    echo [SUCCESS] PD2_MapReveal.exe created.
    echo.
    echo Launching Map Reveal as Administrator...
    powershell -Command "Start-Process 'PD2_MapReveal.exe' -Verb RunAs"
) else (
    echo [ERROR] Compilation failed.
)

pause
