namespace CasaRosita.Services;
public sealed class LocalOptions
{
    public int Port { get; set; } = 5080;
    public string ConnectionString { get; set; } = @"Server=.\SQLEXPRESS;Database=CasaRosita;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connect Timeout=10";
    public string BackupFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CasaRosita", "Backups");
    public int BackupRetention { get; set; } = 14;
    public int BackupHour { get; set; } = 20;
}
