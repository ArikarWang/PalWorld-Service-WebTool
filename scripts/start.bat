@echo off
cd /d "%~dp0"
title PalWorld Service
echo ========================================
echo   PalWorld Service
echo   Close this window to stop the service
echo ========================================
echo.
set ASPNETCORE_ENVIRONMENT=Production
PalWorldService.exe
echo.
echo Service stopped.
pause
