param([switch]$KeepRunning,[string]$Database='CasaRosita_Validation')
$ErrorActionPreference='Stop'
if($Database -notmatch '^CasaRosita_Validation(?:_[A-Za-z0-9]+)?$'){throw 'La prueba solo admite bases CasaRosita_Validation.'}
. "$PSScriptRoot\Sql.ps1"
$root=(Resolve-Path "$PSScriptRoot\..").Path
$data=Join-Path $root $(if($Database -eq 'CasaRosita_Validation'){'.local\validation'}else{".local\$Database"})
$publish=Join-Path $root 'artifacts\publish'
New-Item -ItemType Directory -Path $data -Force | Out-Null
$master='Server=.\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$backupPath=(Invoke-RositaSql $master "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath')) AS Folder").Rows[0].Folder
if(-not $backupPath){$backupPath=(Invoke-RositaSql $master "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath')) AS Folder").Rows[0].Folder}
@{Local=@{Port=5089;ConnectionString="Server=.\SQLEXPRESS;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False";BackupFolder=$backupPath;BackupRetention=14;BackupHour=23}} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $data 'local.json') -Encoding UTF8
$exe=Join-Path $publish 'CasaRosita.exe'
& $exe --migrate --DataRoot $data --contentRoot $publish
if($LASTEXITCODE -ne 0){throw 'Falló migración de la base de validación.'}
$process=Start-Process -FilePath $exe -ArgumentList @('--DataRoot',('"'+$data+'"'),'--contentRoot',('"'+$publish+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $data 'stdout.log') -RedirectStandardError (Join-Path $data 'stderr.log')
try{
    $url='http://127.0.0.1:5089'
    $healthy=$false
    for($attempt=0;$attempt -lt 30;$attempt++){try{if((Invoke-WebRequest "$url/salud" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200){$healthy=$true;break}}catch{};Start-Sleep -Milliseconds 500}
    if(-not $healthy){throw 'No inició la aplicación publicada.'}
    Write-Host 'OK: publicación self-contained inicia y conecta con SQL.'
    $session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    function Token($page){$match=[regex]::Match($page,'name="__RequestVerificationToken"[^>]*value="([^"]+)"');if(-not $match.Success){throw 'No se encontró token antifalsificación.'};return [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)}
    $keyPath=Join-Path $data 'primer-inicio.key'
    $passwordPath=Join-Path $data 'validation-password.txt'
    if(Test-Path -LiteralPath $keyPath){
        $bytes=New-Object byte[] 24;[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
        $password='Aa1'+[Convert]::ToBase64String($bytes)
        $password | Set-Content -LiteralPath $passwordPath
        $setup=Invoke-WebRequest "$url/primer-inicio" -WebSession $session -UseBasicParsing
        [void](Invoke-WebRequest "$url/crear-administrador" -Method Post -WebSession $session -UseBasicParsing -Body @{__RequestVerificationToken=(Token $setup.Content);codigo=(Get-Content -LiteralPath $keyPath -Raw).Trim();usuario='validacion';clave=$password})
        if(Test-Path -LiteralPath $keyPath){throw 'Falló primer administrador.'}
        Write-Host 'OK: primer administrador local con código de instalación.'
    }
    $password=(Get-Content -LiteralPath $passwordPath -Raw).Trim()
    $login=Invoke-WebRequest "$url/ingresar" -WebSession $session -UseBasicParsing
    $result=Invoke-WebRequest "$url/sesion" -Method Post -WebSession $session -UseBasicParsing -Body @{__RequestVerificationToken=(Token $login.Content);usuario='validacion';clave=$password}
    if($result.Content -notmatch 'Nueva venta' -or $result.Content -match 'No se pudo completar'){throw 'Falló autenticación.'}
    Write-Host 'OK: autenticación por cookie con antifalsificación.'
    foreach($route in @('/','/venta','/productos','/ventas','/caja','/informes','/clientes','/configuracion')){
        $page=Invoke-WebRequest ($url+$route) -WebSession $session -UseBasicParsing
        if($page.StatusCode -ne 200 -or $page.Content -match 'An error occurred|Se produjo un error inesperado|blazor-error-boundary'){throw "Falló $route"}
        Write-Host "OK: renderizado autenticado $route"
    }
    $csv=Invoke-WebRequest "$url/informes.csv?type=stock" -WebSession $session -UseBasicParsing
    if($csv.Headers['Content-Type'] -notmatch 'text/csv'){throw 'Falló CSV.'}
    Write-Host 'OK: exportación CSV autenticada.'
    try{Invoke-WebRequest "$url/sesion" -Method Post -Body @{usuario='validacion';clave=$password} -UseBasicParsing | Out-Null;throw 'Se aceptó POST sin token.'}catch{if($_.Exception.Response.StatusCode -ne 400){throw}}
    Write-Host 'OK: formulario sin antifalsificación rechazado.'
    if($KeepRunning){$process.Id | Set-Content -LiteralPath (Join-Path $data 'app.pid');Write-Host "Validación disponible en $url; usuario validacion; contraseña temporal en .local\validation\validation-password.txt. PID $($process.Id)"}
}finally{if(-not $KeepRunning -and -not $process.HasExited){Stop-Process -Id $process.Id}}
