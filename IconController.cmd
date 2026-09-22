@echo off
if not exist "%~dp0build\IconController.exe" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1"
  if errorlevel 1 (
    pause
    exit /b 1
  )
)
start "" "%~dp0build\IconController.exe"
