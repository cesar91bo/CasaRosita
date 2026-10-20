param([string]$Instance='SQLEXPRESS')
$ErrorActionPreference='Stop'
$env:CASAROSITA_TEST_SQL="Server=.\$Instance;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False"
dotnet run --project "$PSScriptRoot\..\tests\CasaRosita.Checks"
if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas.'}
