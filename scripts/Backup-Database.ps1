[CmdletBinding()]
param(
    [string]$SqlServerInstance = 'localhost',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,127}$')][string]$Database = 'TrainingManagement',
    [Parameter(Mandatory)][string]$ServerBackupDirectory
)
$ErrorActionPreference = 'Stop'
$backupFile = Join-Path $ServerBackupDirectory ($Database + '-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss') + '-' + [Guid]::NewGuid().ToString('N') + '.bak')
$escapedPath = $backupFile.Replace("'", "''")
# The directory is on the database server and must be writable by its service account.
$query = "BACKUP DATABASE [$Database] TO DISK=N'$escapedPath' WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$escapedPath' WITH CHECKSUM;"
& sqlcmd -S $SqlServerInstance -E -N -C -b -l 10 -t 600 -Q $query
if ($LASTEXITCODE -ne 0) { throw 'Backup or backup verification failed.' }
Write-Output "Verified backup: $backupFile"
