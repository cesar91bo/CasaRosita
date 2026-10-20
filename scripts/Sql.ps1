function Invoke-RositaSql {
    param([string]$ConnectionString,[string]$Query,[hashtable]$Parameters=@{})
    $connection=New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    try {
        $connection.Open()
        $command=$connection.CreateCommand();$command.CommandTimeout=600;$command.CommandText=$Query
        foreach($name in $Parameters.Keys){[void]$command.Parameters.AddWithValue($name,$Parameters[$name])}
        $table=New-Object System.Data.DataTable
        $reader=$command.ExecuteReader();$table.Load($reader);$reader.Dispose()
        return ,$table
    } finally {$connection.Dispose()}
}
function Assert-RositaAdmin {
    $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Abrí PowerShell como administrador para instalar o restaurar.'}
}
