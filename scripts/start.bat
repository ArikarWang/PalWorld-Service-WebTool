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
set EXITCODE=%ERRORLEVEL%
echo.
if not "%EXITCODE%"=="0" (
  echo Service exited with code %EXITCODE%
  echo.
  pause
  exit /b %EXITCODE%
)
echo Service stopped. Window will close.
timeout /t 2 /nobreak >nul
exit /b 0
