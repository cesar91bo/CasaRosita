param(
    [Parameter(Mandatory=$true)][string]$PublishFolder,
    [string]$InstallRoot="$env:ProgramFiles\CasaRosita",
    [string]$DataRoot="$env:ProgramData\CasaRosita",
    [string]$Instance='SQLEXPRESS',
    [ValidateRange(1024,65535)][int]$Port=5080,
    [string]$BackupFolder,
    [switch]$OpenBrowser
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Sql.ps1"
Assert-RositaAdmin
if($Instance -notmatch '^[A-Za-z0-9_]+$'){throw 'Nombre de instancia inválido.'}
$PublishFolder=(Resolve-Path -LiteralPath $PublishFolder).Path
if(-not(Test-Path -LiteralPath (Join-Path $PublishFolder 'CasaRosita.exe'))){throw 'Falta una publicación self-contained win-x64.'}
if(-not(Test-Path -LiteralPath (Join-Path $PublishFolder 'manifest.json'))){throw 'Falta el manifiesto generado por Publish.ps1.'}
foreach($entry in (Get-Content -LiteralPath (Join-Path $PublishFolder 'manifest.json') -Raw | ConvertFrom-Json)){
    $file=Join-Path $PublishFolder $entry.Path
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.Sha256){throw "Archivo dañado: $($entry.Path)"}
}
$sqlService=Get-Service -Name ('MSSQL$'+$Instance) -ErrorAction Stop
if($sqlService.Status -ne 'Running'){Start-Service -Name $sqlService.Name}
$master="Server=.\$Instance;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
[void](Invoke-RositaSql $master 'SELECT 1')
New-Item -ItemType Directory -Path $InstallRoot,$DataRoot -Force | Out-Null
$configPath=Join-Path $DataRoot 'local.json'
if(Test-Path -LiteralPath $configPath){
    $config=Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $priorBackupFolder=$config.Local.BackupFolder
    if($PSBoundParameters.ContainsKey('Port')){$config.Local.Port=$Port}
    if($BackupFolder){$config.Local.BackupFolder=$BackupFolder}
}else{
    if(-not $BackupFolder){$BackupFolder=Join-Path $DataRoot 'Backups'}
    $config=@{Local=@{Port=$Port;ConnectionString="Server=.\$Instance;Database=CasaRosita;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False";BackupFolder=$BackupFolder;BackupRetention=14;BackupHour=20}}
}
$Port=$config.Local.Port
$cs=New-Object System.Data.SqlClient.SqlConnectionStringBuilder $config.Local.ConnectionString
if($cs.DataSource -ne ".\$Instance" -or $cs.InitialCatalog -ne 'CasaRosita'){throw 'La configuración existente no coincide con la instancia indicada o la base CasaRosita.'}
$existing=Get-Service -Name CasaRosita -ErrorAction SilentlyContinue
if($existing){Stop-Service -Name CasaRosita -Force;$existing.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
$databaseExists=Invoke-RositaSql $master "SELECT name FROM sys.databases WHERE name=N'CasaRosita'"
if($databaseExists.Rows.Count -gt 0){
    $safetyFolder=if($priorBackupFolder){$priorBackupFolder}else{$config.Local.BackupFolder}
    $previous=Join-Path $safetyFolder ("CasaRosita-previa-"+(Get-Date -Format yyyyMMdd-HHmmss)+'.bak')
    [void](Invoke-RositaSql $master 'BACKUP DATABASE [CasaRosita] TO DISK=@p WITH COPY_ONLY, INIT, CHECKSUM; RESTORE VERIFYONLY FROM DISK=@p WITH CHECKSUM;' @{'@p'=$previous})
    Write-Host "Respaldo previo verificado: $previous"
}
$release=Join-Path $InstallRoot ('releases\'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $release -Force | Out-Null
Copy-Item -Path (Join-Path $PublishFolder '*') -Destination $release -Recurse -Force
$exe=Join-Path $release 'CasaRosita.exe'
$binary='"'+$exe+'" --contentRoot "'+$release+'" --DataRoot "'+$DataRoot+'"'
if(-not $existing){New-Service -Name CasaRosita -DisplayName 'Casa Rosita' -BinaryPathName $binary -StartupType Automatic -DependsOn $sqlService.Name | Out-Null}
& sc.exe config CasaRosita obj= 'NT SERVICE\CasaRosita' | Out-Null
if($LASTEXITCODE -ne 0){throw 'No se pudo configurar la identidad del servicio.'}
& sc.exe sidtype CasaRosita unrestricted | Out-Null
$sqlIdentity=(Get-CimInstance Win32_Service -Filter "Name='$($sqlService.Name)'").StartName
New-Item -ItemType Directory -Path $config.Local.BackupFolder -Force | Out-Null
& icacls.exe $DataRoot /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' 'NT SERVICE\CasaRosita:(OI)(CI)M' | Out-Null
if($LASTEXITCODE -ne 0){throw 'No se pudieron proteger los datos.'}
& icacls.exe $config.Local.BackupFolder /grant ($sqlIdentity+':(OI)(CI)M') 'NT SERVICE\CasaRosita:(OI)(CI)M' | Out-Null
if($LASTEXITCODE -ne 0){throw 'No se pudieron asignar permisos de backup a SQL Server.'}
$config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $configPath -Encoding UTF8
& $exe --migrate --DataRoot $DataRoot --contentRoot $release
if($LASTEXITCODE -ne 0){throw 'Migración fallida. Servicio detenido; conservá el respaldo y la versión anterior. Ver RECUPERACION.md.'}
[void](Invoke-RositaSql $master "IF NOT EXISTS(SELECT 1 FROM sys.server_principals WHERE name=N'NT SERVICE\CasaRosita') CREATE LOGIN [NT SERVICE\CasaRosita] FROM WINDOWS; USE [CasaRosita]; IF NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE name=N'NT SERVICE\CasaRosita') CREATE USER [NT SERVICE\CasaRosita] FOR LOGIN [NT SERVICE\CasaRosita]; ALTER ROLE db_datareader ADD MEMBER [NT SERVICE\CasaRosita]; ALTER ROLE db_datawriter ADD MEMBER [NT SERVICE\CasaRosita]; ALTER ROLE db_backupoperator ADD MEMBER [NT SERVICE\CasaRosita]; GRANT VIEW DEFINITION TO [NT SERVICE\CasaRosita];")
# VERIFYONLY requires CREATE DATABASE permission on modern SQL Server. Grant it only through a signed operational procedure in hardened deployments; this local MVP uses the runtime backup operator plus this explicit permission.
[void](Invoke-RositaSql $master 'GRANT CREATE ANY DATABASE TO [NT SERVICE\CasaRosita]')
& sc.exe config CasaRosita binPath= $binary start= auto depend= $sqlService.Name | Out-Null
if($LASTEXITCODE -ne 0){throw 'No se pudo actualizar la ruta del servicio.'}
& sc.exe failure CasaRosita reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
Start-Service -Name CasaRosita
(Get-Service CasaRosita).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
$url="http://127.0.0.1:$Port"
$healthy=$false
for($attempt=0;$attempt -lt 30;$attempt++){
    try{$response=Invoke-WebRequest "$url/salud" -UseBasicParsing -TimeoutSec 3;if($response.StatusCode -eq 200){$healthy=$true;break}}catch{}
    Start-Sleep -Seconds 1
}
if(-not $healthy){throw "El servicio no responde con conexión SQL. Revisá $DataRoot\Logs. La instalación NO está validada."}
$page=Invoke-WebRequest "$url/ingresar" -UseBasicParsing
if($page.StatusCode -ne 200 -or $page.Content -notmatch 'Casa Rosita'){throw 'Falló la verificación de la pantalla de ingreso.'}
$shortcut=Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Casa Rosita.url'
"[InternetShortcut]`r`nURL=$url/venta" | Set-Content -LiteralPath $shortcut -Encoding ASCII
Write-Host "Servicio, SQL y pantalla de ingreso verificados. Abrí $url/primer-inicio y realizá la prueba del navegador e impresión."
Write-Host "El código inicial está en $DataRoot\primer-inicio.key (solo administradores)."
if($OpenBrowser){Start-Process $url -WindowStyle Hidden}
