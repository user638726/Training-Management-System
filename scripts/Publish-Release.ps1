[CmdletBinding()]
param([switch]$SkipInstall)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releasePath = Join-Path $projectRoot ('.runtime\releases\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
Push-Location (Join-Path $projectRoot 'frontend')
try {
    if (-not $SkipInstall) { & npm.cmd ci --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' } }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Angular production build failed.' }
} finally { Pop-Location }
& dotnet publish (Join-Path $projectRoot 'backend\TrainingManagement.Api.csproj') -c Release -o $releasePath --nologo
if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
$webRoot = Join-Path $releasePath 'wwwroot'
New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'frontend\dist\training-management-web\browser') | Copy-Item -Destination $webRoot -Recurse
Push-Location (Join-Path $projectRoot 'backend')
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Migration tool restore failed.' }
    & dotnet tool run dotnet-ef migrations script --configuration Release --no-build --idempotent --output (Join-Path $releasePath 'migrate.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Migration script generation failed.' }
} finally { Pop-Location }
Write-Output "Release package: $releasePath"
Write-Output 'Back up SQL Server, review and apply migrate.sql, configure production secrets and HTTPS, then start TrainingManagement.Api.dll.'
