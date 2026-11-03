param(
  [ValidateSet('Publish','Pause','Resume')][string]$Mode='Publish',
  [string]$CandidateFile,
  [string]$Message='Administrace dočasně pozastavila aktualizace. Sleduj Discord.',
  [string]$Gh='gh',
  [switch]$Approved
)
$ErrorActionPreference='Stop'
$repo='kronikykralovstvi/kroniky-launcher'
$publicKey='MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE55iJweP0Rtre2qMHdXLv46/dyeUhKFa4nFTjDhqbivdLlHw0G7cEvOyuH3dDEVFD5vE43fmsOZNCY9s2CEOpTg=='
function Call-Gh { param([string[]]$Arguments) $out=& $Gh @Arguments; if($LASTEXITCODE -ne 0){throw 'GitHub request failed.'};return ($out -join "`n") }
$remote=Call-Gh -Arguments @('api',"repos/$repo/contents/trusted-release.json") | ConvertFrom-Json
$envelope=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($remote.content)) | ConvertFrom-Json
$key=[Security.Cryptography.ECDsa]::Create()
try {
  $read=0;$key.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($publicKey),[ref]$read)
  $bytes=[Convert]::FromBase64String($envelope.payload)
  if(-not $key.VerifyData($bytes,[Convert]::FromBase64String($envelope.signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)){throw 'Current release signature is invalid. Stop and recover GitHub access.'}
  $current=[Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
} finally {$key.Dispose()}
$local=Join-Path $env:LOCALAPPDATA 'KronikyKralovstvi/Signing'
[IO.Directory]::CreateDirectory($local)|Out-Null
$counter=Join-Path $local 'last-sequence.txt'
$last=if(Test-Path $counter){[long](Get-Content $counter -Raw)}else{0}
if($Mode -eq 'Publish'){
  if(-not $CandidateFile){throw 'CandidateFile is required. Download release-request.json from the prepared release.'}
  $next=Get-Content -LiteralPath $CandidateFile -Raw | ConvertFrom-Json
}else{$next=$current}
$next.schemaVersion=1
$next.sequence=[Math]::Max([long]$current.sequence,$last)+1
$next.issuedAt=[DateTimeOffset]::UtcNow.ToString('o')
$next.paused=($Mode -eq 'Pause');$next.message=if($next.paused){$Message}else{''}
if(-not $next.launcher -or -not $next.mods.files -or $next.launcher.updaterSha256 -ne 'e89210599fdef8df1b64be85e4f99ca51fb9cc5ee940a57dafc13bae4ef8f1a4'){throw 'Invalid release request or changed updater.'}
foreach($url in @($next.launcher.launcherUrl,$next.launcher.updaterUrl)+@($next.mods.files.url)){
  $uri=[uri]$url
  if($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or $uri.Port -ne 443 -or $uri.UserInfo -or -not $uri.AbsolutePath.StartsWith('/kronikykralovstvi/kroniky-launcher/releases/download/')){throw 'Forbidden update source.'}
}
Write-Host "Sequence: $($next.sequence), launcher: $($next.launcher.version), mods: $($next.mods.version), files: $(@($next.mods.files).Count), pause: $($next.paused)"
Write-Host "Launcher SHA-256: $($next.launcher.launcherSha256)"
Write-Host "Source: $($next.launcher.launcherUrl)"
Write-Host 'Approve only a release whose source and files you reviewed. A signature authorizes installation on player PCs.'
if(-not $Approved -and (Read-Host 'Napis VYDAT pro podepsani a zverejneni') -cne 'VYDAT'){throw 'Cancelled.'}
$request=Join-Path $local 'release-request.json';$signed=Join-Path $local 'trusted-release.json'
[IO.File]::WriteAllText($request,($next|ConvertTo-Json -Depth 50),[Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'Sign-Release.ps1') -InputJson $request -OutputJson $signed
if(-not (Test-Path $signed)){throw 'Signing failed.'}
[IO.File]::WriteAllText($counter,[string]$next.sequence)
$payload=@{message="Publish signed release $($next.sequence) ($Mode)";branch='main';sha=$remote.sha;content=[Convert]::ToBase64String([IO.File]::ReadAllBytes($signed))}
$apiRequest=Join-Path $local 'github-request.json';[IO.File]::WriteAllText($apiRequest,($payload|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Call-Gh -Arguments @('api','--method','PUT',"repos/$repo/contents/trusted-release.json",'--input',$apiRequest)|Out-Null
Write-Host 'Signed release published. Keep signing tools and the private key off GitHub.'
