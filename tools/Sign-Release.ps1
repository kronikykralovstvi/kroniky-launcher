param(
  [string]$InputJson,
  [string]$OutputJson,
  [switch]$InitializeKey,
  [string]$RecoveryBackup,
  [string]$KeyFile = (Join-Path $env:LOCALAPPDATA 'KronikyKralovstvi/Signing/release-key.dpapi')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$ecdsa = [Security.Cryptography.ECDsa]::Create()
$ecdsa.GenerateKey([Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
try {
  if (-not (Test-Path -LiteralPath $KeyFile)) {
    if (-not $InitializeKey) { throw 'Signing key missing. Do not create a replacement key for existing clients.' }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($KeyFile)) | Out-Null
    $secret = $ecdsa.ExportPkcs8PrivateKey()
    try {
      $protected = [Security.Cryptography.ProtectedData]::Protect($secret, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
      $stream = [IO.File]::Open($KeyFile, [IO.FileMode]::CreateNew)
      try { $stream.Write($protected); $stream.Flush($true) } finally { $stream.Dispose() }
    } finally { [Array]::Clear($secret,0,$secret.Length) }
  } else {
    $secret = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($KeyFile), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { $read = 0; $ecdsa.ImportPkcs8PrivateKey($secret, [ref]$read) } finally { [Array]::Clear($secret,0,$secret.Length) }
  }
  $publicKey = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())
  if ($InitializeKey) { Write-Output $publicKey }
  if ($RecoveryBackup) {
    if (Test-Path -LiteralPath $RecoveryBackup) { throw 'Backup already exists; choose another filename.' }
    $password = Read-Host 'Heslo pro sifrovanou offline zalohu klice' -AsSecureString
    $confirm = Read-Host 'Zopakuj heslo' -AsSecureString
    $p1=[Net.NetworkCredential]::new('', $password).Password
    $p2=[Net.NetworkCredential]::new('', $confirm).Password
    if ($p1.Length -lt 16 -or $p1 -cne $p2) { throw 'Use a matching password of at least 16 characters.' }
    try {
      $pbe=[Security.Cryptography.PbeParameters]::new([Security.Cryptography.PbeEncryptionAlgorithm]::Aes256Cbc,[Security.Cryptography.HashAlgorithmName]::SHA256,600000)
      [IO.File]::WriteAllText($RecoveryBackup,$ecdsa.ExportEncryptedPkcs8PrivateKeyPem($p1,$pbe))
    } finally { $p1=$null;$p2=$null;$password.Dispose();$confirm.Dispose() }
    Write-Output 'Encrypted recovery backup created. Keep it offline, outside GitHub.'
  }
  if ($InputJson) {
    if (-not $OutputJson) { throw 'OutputJson is required.' }
    $bytes=[IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $InputJson))
    $request=[Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
    if ($request.schemaVersion -ne 1 -or $request.sequence -lt 1 -or -not $request.launcher -or -not $request.mods) { throw 'Invalid release request.' }
    $signature=$ecdsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    $envelope=@{payload=[Convert]::ToBase64String($bytes);signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json -Compress
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputJson),$envelope,[Text.UTF8Encoding]::new($false))
    Write-Output "Signed sequence $($request.sequence), launcher $($request.launcher.version), mods $($request.mods.version), paused=$($request.paused)."
  }
} finally { if ($ecdsa) { $ecdsa.Dispose() } }
