[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtimePath = Join-Path $projectRoot '.runtime'
$statePath = Join-Path $runtimePath 'processes.json'
$backendPath = Join-Path $projectRoot 'backend'
$frontendPath = Join-Path $projectRoot 'frontend'
$cliPath = Join-Path $frontendPath 'node_modules/@angular/cli/bin/ng.js'

function Test-PortOccupied([int]$Port) {
  $client = [System.Net.Sockets.TcpClient]::new()
  try {
    $connection = $client.ConnectAsync('localhost', $Port)
    if ($connection.Wait(1000)) { return $client.Connected }
    return $false
  } catch { return $false } finally { $client.Dispose() }
}

if (Test-Path -LiteralPath $statePath) {
  throw 'Found an existing dev session. Run scripts/Stop-Dev.ps1 before starting again.'
}
foreach ($port in @(5080, 4200)) {
  if (Test-PortOccupied $port) { throw "Port $port is occupied. Stop the existing service first." }
}

$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
$nodePath = (Get-Command node -ErrorAction Stop).Source
New-Item -ItemType Directory -Path $runtimePath -Force | Out-Null
& (Join-Path $PSScriptRoot 'Initialize-DevSecrets.ps1')

if (-not (Test-Path -LiteralPath $cliPath)) {
  Push-Location $frontendPath
  try {
    & npm.cmd ci --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
  } finally { Pop-Location }
}
if (-not $SkipBuild) {
  & $dotnetPath build (Join-Path $backendPath 'TrainingManagement.Api.csproj') --nologo
  if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
}
$apiDll = Join-Path $backendPath 'bin/Debug/net10.0/TrainingManagement.Api.dll'
if (-not (Test-Path -LiteralPath $apiDll)) { throw 'Backend DLL is missing. Run without -SkipBuild.' }

$ownedProcesses = @()
Add-Type -AssemblyName System.Net.Http
$httpHandler = [System.Net.Http.HttpClientHandler]::new()
$httpHandler.UseProxy = $false
$httpClient = [System.Net.Http.HttpClient]::new($httpHandler)
$httpClient.Timeout = [TimeSpan]::FromSeconds(3)
$previousLegacyImportPath = [Environment]::GetEnvironmentVariable('LegacyImportPath', 'Process')
try {
  $legacyExportPath = Join-Path $runtimePath 'legacy-export.json'
  if ($null -eq $previousLegacyImportPath -and (Test-Path -LiteralPath $legacyExportPath)) {
    $env:LegacyImportPath = $legacyExportPath
  }
  $apiProcess = Start-Process -FilePath $dotnetPath -ArgumentList @(('"' + $apiDll + '"'), '--urls', 'http://localhost:5080', '--environment', 'Development') -WorkingDirectory $backendPath -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtimePath 'backend.log') -RedirectStandardError (Join-Path $runtimePath 'backend.error.log')
  $ownedProcesses += $apiProcess
  $webProcess = Start-Process -FilePath $nodePath -ArgumentList @(('"' + $cliPath + '"'), 'serve', '--host', '127.0.0.1', '--port', '4200') -WorkingDirectory $frontendPath -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runtimePath 'frontend.log') -RedirectStandardError (Join-Path $runtimePath 'frontend.error.log')
  $ownedProcesses += $webProcess
  $state = @($ownedProcesses | ForEach-Object { @{ id = $_.Id; startTimeTicks = $_.StartTime.ToUniversalTime().Ticks; name = $_.ProcessName } })
  $state | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $statePath -Encoding utf8

  $ready = $false
  for ($attempt = 0; $attempt -lt 90; $attempt++) {
    foreach ($ownedProcess in $ownedProcesses) {
      if ($ownedProcess.HasExited) { throw "Service exited. Read logs in $runtimePath." }
    }
    try {
      $apiResponse = $httpClient.GetAsync('http://127.0.0.1:5080/api/health').GetAwaiter().GetResult()
      $webResponse = $httpClient.GetAsync('http://127.0.0.1:4200').GetAwaiter().GetResult()
      try {
        if ($apiResponse.IsSuccessStatusCode -and $webResponse.IsSuccessStatusCode) { $ready = $true; break }
      } finally { $apiResponse.Dispose(); $webResponse.Dispose() }
    } catch { Start-Sleep -Seconds 1 }
  }
  if (-not $ready) { throw "Service startup timed out. Read logs in $runtimePath." }
  Write-Output 'Frontend: http://localhost:4200'
  Write-Output 'API:      http://localhost:5080/api/health'
  Write-Output "Logs:     $runtimePath"
  Write-Output 'Stop:     powershell -ExecutionPolicy Bypass -File scripts/Stop-Dev.ps1'
} catch {
  foreach ($ownedProcess in $ownedProcesses) {
    if (-not $ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id -Force -ErrorAction SilentlyContinue }
  }
  if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath }
  throw
} finally {
  [Environment]::SetEnvironmentVariable('LegacyImportPath', $previousLegacyImportPath, 'Process')
  $httpClient.Dispose()
}
