using CasaRosita.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace CasaRosita.Services;
public sealed class BackupService(IDbContextFactory<StoreDb> factory, LocalOptions options, ILogger<BackupService> logger)
{
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task<BackupRecord> Run()
    {
        await gate.WaitAsync();
        var record=new BackupRecord { Created=DateTimeOffset.Now };
        try
        {
            Directory.CreateDirectory(options.BackupFolder);
            record.FileName=Path.Combine(Path.GetFullPath(options.BackupFolder),$"CasaRosita-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak");
            await using var connection=new SqlConnection(options.ConnectionString); await connection.OpenAsync();
            var name="["+connection.Database.Replace("]","]]")+"]";
            await using var cmd=connection.CreateCommand(); cmd.CommandTimeout=600;
            cmd.CommandText=$"BACKUP DATABASE {name} TO DISK = @path WITH COPY_ONLY, INIT, CHECKSUM; RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;";
            cmd.Parameters.AddWithValue("@path",record.FileName); await cmd.ExecuteNonQueryAsync(); record.Success=true;
        }
        catch(Exception ex) { record.Error="No se pudo crear o verificar el respaldo. Revisá la carpeta, espacio y permisos de SQL Server."; logger.LogError(ex,"Falló respaldo SQL"); }
        try
        {
            await using var db=await factory.CreateDbContextAsync(); db.Backups.Add(record); await db.SaveChangesAsync();
            if(record.Success)
            {
                var old=await db.Backups.Where(x=>x.Success).OrderByDescending(x=>x.Created).Skip(options.BackupRetention).ToListAsync();
                foreach(var item in old)
                    if(item.FileName is not null && Path.GetDirectoryName(item.FileName)==Path.GetFullPath(options.BackupFolder) && Path.GetFileName(item.FileName).StartsWith("CasaRosita-")) File.Delete(item.FileName);
            }
        }
        finally { gate.Release(); }
        return record;
    }
}
public sealed class BackupWorker(BackupService backup, IDbContextFactory<StoreDb> factory, LocalOptions options, ILogger<BackupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        DateTimeOffset nextAttempt=DateTimeOffset.MinValue;
        while(!token.IsCancellationRequested)
        {
            try
            {
                await using var db=await factory.CreateDbContextAsync();
                var last=await db.Backups.Where(x=>x.Success).OrderByDescending(x=>x.Created).FirstOrDefaultAsync(token);
                var now=DateTimeOffset.Now;
                if(now>=nextAttempt && (last is null || last.Created.LocalDateTime.Date < now.LocalDateTime.Date) && (now.Hour>=options.BackupHour || last is null || last.Created.LocalDateTime.Date<now.LocalDateTime.Date.AddDays(-1)))
                { nextAttempt=now.AddHours(1); await backup.Run(); }
            }
            catch(Exception ex) when(ex is not OperationCanceledException) { log.LogError(ex,"Error en respaldo automático"); }
            await Task.Delay(TimeSpan.FromMinutes(1),token);
        }
    }
}
