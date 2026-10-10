[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$statePath = Join-Path $projectRoot '.runtime/processes.json'
if (-not (Test-Path -LiteralPath $statePath)) {
  Write-Output 'No managed dev session is running.'
  return
}

$records = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
foreach ($record in $records) {
  $process = Get-Process -Id $record.id -ErrorAction SilentlyContinue
  if ($null -eq $process) { continue }
  if ($process.ProcessName -ne $record.name -or $process.StartTime.ToUniversalTime().Ticks -ne [long]$record.startTimeTicks) {
    Write-Warning "Process $($record.id) has changed; it was left untouched."
    continue
  }
  Stop-Process -Id $process.Id -Force
  Write-Output "Stopped $($record.name) ($($record.id))."
}
Remove-Item -LiteralPath $statePath
