[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param(
    [Parameter(Mandatory=$true)][string]$BackupFile,
    [string]$DataRoot="$env:ProgramData\CasaRosita",
    [string]$Instance='SQLEXPRESS',
    [Parameter(Mandatory=$true)][ValidateSet('RESTAURAR CASA ROSITA')][string]$Confirmation
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Sql.ps1"
Assert-RositaAdmin
if($Instance -notmatch '^[A-Za-z0-9_]+$'){throw 'Instancia inválida.'}
$BackupFile=(Resolve-Path -LiteralPath $BackupFile).Path
$master="Server=.\$Instance;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
[void](Invoke-RositaSql $master 'RESTORE VERIFYONLY FROM DISK=@p WITH CHECKSUM' @{'@p'=$BackupFile})
$header=Invoke-RositaSql $master 'RESTORE HEADERONLY FROM DISK=@p' @{'@p'=$BackupFile}
if($header.Rows.Count -ne 1 -or $header.Rows[0].DatabaseName -ne 'CasaRosita'){throw 'El archivo no contiene un único respaldo de CasaRosita.'}
if(-not $PSCmdlet.ShouldProcess('Base CasaRosita','Reemplazar los datos actuales por el respaldo verificado')){return}
$service=Get-Service CasaRosita -ErrorAction Stop
Stop-Service CasaRosita -Force;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
$exists=Invoke-RositaSql $master "SELECT name FROM sys.databases WHERE name=N'CasaRosita'"
if($exists.Rows.Count -gt 0){
    $config=Get-Content -LiteralPath (Join-Path $DataRoot 'local.json') -Raw | ConvertFrom-Json
    $safety=Join-Path $config.Local.BackupFolder ('CasaRosita-antes-restaurar-'+(Get-Date -Format yyyyMMdd-HHmmss)+'.bak')
    [void](Invoke-RositaSql $master 'BACKUP DATABASE [CasaRosita] TO DISK=@p WITH COPY_ONLY,INIT,CHECKSUM; RESTORE VERIFYONLY FROM DISK=@p WITH CHECKSUM;' @{'@p'=$safety})
    Write-Host "Copia del estado anterior: $safety"
}
$files=Invoke-RositaSql $master 'RESTORE FILELISTONLY FROM DISK=@p' @{'@p'=$BackupFile}
$paths=Invoke-RositaSql $master "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath')) AS DataPath, CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultLogPath')) AS LogPath"
$moves=@();$index=0
foreach($file in $files.Rows){
    if($file.Type -notin @('D','L')){throw 'Tipo de archivo SQL no soportado por este procedimiento.'}
    $folder=if($file.Type -eq 'L'){$paths.Rows[0].LogPath}else{$paths.Rows[0].DataPath}
    $extension=if($file.Type -eq 'L'){'.ldf'}else{'.mdf'}
    $target=Join-Path $folder ("CasaRosita_Restored$index"+$extension)
    $moves+="MOVE N'$($file.LogicalName.Replace("'","''"))' TO N'$($target.Replace("'","''"))'";$index++
}
try{
    if($exists.Rows.Count -gt 0){[void](Invoke-RositaSql $master 'ALTER DATABASE [CasaRosita] SET SINGLE_USER WITH ROLLBACK IMMEDIATE')}
    [void](Invoke-RositaSql $master ('RESTORE DATABASE [CasaRosita] FROM DISK=@p WITH REPLACE,CHECKSUM,'+($moves -join ',')) @{'@p'=$BackupFile})
    [void](Invoke-RositaSql $master 'ALTER DATABASE [CasaRosita] SET MULTI_USER; DBCC CHECKDB ([CasaRosita]) WITH NO_INFOMSGS, ALL_ERRORMSGS;')
}catch{
    try{[void](Invoke-RositaSql $master "IF DB_ID(N'CasaRosita') IS NOT NULL ALTER DATABASE [CasaRosita] SET MULTI_USER")}catch{}
    throw 'Restauración o integridad fallida. Servicio detenido: revisá el error SQL y conservá ambas copias antes de intervenir.'
}
Write-Host 'Restauración e integridad verificadas. Revisá la versión de los binarios y ejecutá Install.ps1 para restablecer permisos, migrar si corresponde y validar el arranque.'
