[CmdletBinding()]
param(
    [string]$SqlServerInstance = 'localhost',
    [Parameter(Mandatory)][string]$ServerBackupFile,
    [Parameter(Mandatory)][ValidatePattern('^TrainingManagement_RestoreTest_[A-Za-z0-9_]{1,64}$')][string]$TargetDatabase,
    [Parameter(Mandatory)][string]$ServerDataDirectory,
    [string]$SourceDatabase = 'TrainingManagement'
)
$ErrorActionPreference = 'Stop'
# Only a new database with an explicit RestoreTest prefix is accepted. No REPLACE or DROP is issued.
$file = $ServerBackupFile.Replace("'", "''")
$data = (Join-Path $ServerDataDirectory ($TargetDatabase + '.mdf')).Replace("'", "''")
$log = (Join-Path $ServerDataDirectory ($TargetDatabase + '_log.ldf')).Replace("'", "''")
$source = $SourceDatabase.Replace("'", "''")
$query = "IF DB_ID(N'$TargetDatabase') IS NOT NULL THROW 51000,'Restore target already exists',1; RESTORE DATABASE [$TargetDatabase] FROM DISK=N'$file' WITH MOVE N'$source' TO N'$data', MOVE N'${source}_log' TO N'$log',CHECKSUM; DBCC CHECKDB([$TargetDatabase]) WITH NO_INFOMSGS;"
& sqlcmd -S $SqlServerInstance -E -N -C -b -l 10 -t 600 -Q $query
if ($LASTEXITCODE -ne 0) { throw 'Restore exercise failed; inspect the isolated restore database. Verify backup logical file names using RESTORE FILELISTONLY.' }
Write-Output "Restore exercise completed in isolated database: $TargetDatabase"
