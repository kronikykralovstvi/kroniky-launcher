@echo off
set "KK_PWSH=%ProgramFiles%\PowerShell\7\pwsh.exe"
if not exist "%KK_PWSH%" set "KK_PWSH=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe"
if not exist "%KK_PWSH%" (
  echo Nainstaluj PowerShell 7 z oficialniho webu Microsoftu.
  pause
  exit /b 1
)
"%KK_PWSH%" -NoProfile -File "%~dp0Admin-Menu.ps1"
