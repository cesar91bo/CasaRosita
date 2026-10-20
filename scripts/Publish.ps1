param([string]$Output = "$PSScriptRoot\..\artifacts\publish")
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot '..\src\CasaRosita\CasaRosita.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.13 -p:Version=0.1.0 -o $Output
if($LASTEXITCODE -ne 0){throw 'Falló la publicación.'}
$manifestPath=Join-Path (Resolve-Path -LiteralPath $Output).Path 'manifest.json'
$manifest=Get-ChildItem -LiteralPath $Output -File -Recurse | Where-Object {$_.FullName -ne $manifestPath} | ForEach-Object { [PSCustomObject]@{Path=$_.FullName.Substring((Resolve-Path $Output).Path.Length+1);Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} }
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'manifest.json') -Encoding UTF8
Write-Host "Publicación Windows x64 lista en $Output"
