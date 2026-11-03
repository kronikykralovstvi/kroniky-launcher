$ErrorActionPreference='Stop'
$gh=Join-Path $PSScriptRoot 'bin/gh.exe'
if(-not(Test-Path $gh)){$gh='gh'}
Write-Host 'KRONIKY KRALOVSTVI - SPRAVA PODEPSANYCH AKTUALIZACI'
Write-Host '1 - Schvalit a vydat pripraveny balicek'
Write-Host '2 - Nouzove pozastavit distribuci'
Write-Host '3 - Obnovit distribuci'
Write-Host '4 - Vytvorit sifrovanou offline zalohu klice'
Write-Host '0 - Konec'
try {
 switch(Read-Host 'Volba'){
  '1' {$candidate=(Read-Host 'Cesta ke zkontrolovanemu release-request.json').Trim('"'); & (Join-Path $PSScriptRoot 'Approve-Release.ps1') -CandidateFile $candidate -Gh $gh}
  '2' {$message=Read-Host 'Zprava pro hrace'; & (Join-Path $PSScriptRoot 'Approve-Release.ps1') -Mode Pause -Message $message -Gh $gh}
  '3' {& (Join-Path $PSScriptRoot 'Approve-Release.ps1') -Mode Resume -Gh $gh}
  '4' {$backup=(Read-Host 'Cesta pro zalohu na bezpecnem offline mediu').Trim('"'); & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -RecoveryBackup $backup}
 }
}catch{Write-Host $_.Exception.Message -ForegroundColor Red}
Read-Host 'Enter pro zavreni'
