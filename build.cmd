@echo off
setlocal
cd /d "%~dp0"

where pwsh >nul 2>nul
if %ERRORLEVEL% equ 0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\build.ps1" %*
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\build.ps1" %*
)

if %ERRORLEVEL% neq 0 (
    exit /b %ERRORLEVEL%
)

endlocal
