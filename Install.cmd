@echo off
powershell.exe -NoProfile -File "%~dp0Install-Local.ps1"
if errorlevel 1 (
  echo Installation did not complete. Close Cities: Skylines II and check the message above.
)
pause
