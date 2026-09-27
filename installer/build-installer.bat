@echo off
REM PlanCake installer build script (batch wrapper)
REM Copyright (c) 2026 Oire Software SARL.

echo Building the PlanCake installer...
echo.

REM PowerShell 7 (pwsh) when it is installed, Windows PowerShell otherwise.
set "PS_EXE=powershell.exe"
where /q pwsh.exe
if %ERRORLEVEL% equ 0 set "PS_EXE=pwsh.exe"

"%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Installer.ps1" -OpenOutput %*

if %ERRORLEVEL% neq 0 (
    echo.
    echo Build failed with error code %ERRORLEVEL%
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Build completed successfully!
