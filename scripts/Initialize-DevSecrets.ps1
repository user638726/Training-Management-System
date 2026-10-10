[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtimePath = Join-Path $projectRoot '.runtime'
$secretsPath = Join-Path $runtimePath 'development.secrets.json'
New-Item -ItemType Directory -Path $runtimePath -Force | Out-Null

if (-not (Test-Path -LiteralPath $secretsPath)) {
  $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
  try {
    $keyBytes = New-Object byte[] 64
    $passwordBytes = New-Object byte[] 24
    $random.GetBytes($keyBytes)
    $random.GetBytes($passwordBytes)
    $settings = @{
      Jwt = @{ SigningKey = [Convert]::ToBase64String($keyBytes) }
      BootstrapAdmin = @{
        Username = 'admin'
        Password = [Convert]::ToBase64String($passwordBytes)
        DisplayName = [Regex]::Unescape('\u7cfb\u7d71\u7ba1\u7406\u54e1')
      }
    }
    $settings | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $secretsPath -Encoding utf8
  } finally { $random.Dispose() }
}

$settings = Get-Content -LiteralPath $secretsPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([Text.Encoding]::UTF8.GetByteCount([string]$settings.Jwt.SigningKey) -lt 32) {
  throw 'Development Jwt.SigningKey must contain at least 32 UTF-8 bytes.'
}
if ([string]::IsNullOrWhiteSpace($settings.BootstrapAdmin.Username) -or ([string]$settings.BootstrapAdmin.Password).Length -lt 12) {
  throw 'Development bootstrap administrator settings are incomplete.'
}
Write-Output "Development login settings: $secretsPath"
