using System.Security.Claims;
using CasaRosita.Data;
using CasaRosita.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

var assertions=0;
void Check(bool value,string name){if(!value)throw new Exception("FALLÓ: "+name);Console.WriteLine("OK: "+name);assertions++;}
async Task Reject(Func<Task> action,string name){try{await action();}catch(InvalidOperationException){Check(true,name);return;}catch(UnauthorizedAccessException){Check(true,name);return;}throw new Exception("No se rechazó: "+name);}
Check(Money.Round(1.75m*8000m)==14000m,"1,75 metros × $8.000 = $14.000");
await Reject(()=>{Money.Quantity(1.75m,"Unidad");return Task.CompletedTask;},"Unidades fraccionadas rechazadas");
Check(Reports.Cell("=cmd()").StartsWith("\"'"),"CSV neutraliza fórmulas");
var source=Environment.GetEnvironmentVariable("CASAROSITA_TEST_SQL");
if(string.IsNullOrWhiteSpace(source))throw new Exception("Falta CASAROSITA_TEST_SQL. Las pruebas SQL no se omiten silenciosamente.");
var cs=new SqlConnectionStringBuilder(source){InitialCatalog="CasaRosita_Test_"+Guid.NewGuid().ToString("N")};
var options=new DbContextOptionsBuilder<StoreDb>().UseSqlServer(cs.ConnectionString).Options;
IDbContextFactory<StoreDb> factory=new Factory(options);
Console.WriteLine("Base aislada: "+cs.InitialCatalog);
try
{
    await using(var db=await factory.CreateDbContextAsync())
    {
        await db.Database.MigrateAsync();
        db.Users.AddRange(new LocalUser{Id="admin",UserName="admin",NormalizedUserName="ADMIN"},new LocalUser{Id="seller",UserName="seller",NormalizedUserName="SELLER"});
        db.Roles.Add(new IdentityRole{Id="role-admin",Name="Administrador",NormalizedName="ADMINISTRADOR"});
        db.UserRoles.Add(new IdentityUserRole<string>{UserId="admin",RoleId="role-admin"});
        await db.SaveChangesAsync();
        Check(await db.Categories.CountAsync()==4,"Migración y categorías iniciales SQL");
    }
    var actor=new Actor(new Auth("admin"),factory);var seller=new Actor(new Auth("seller"),factory);
    var commerce=new Commerce(factory,actor);var sales=new Commerce(factory,seller);var catalog=new Catalog(factory,actor);
    await catalog.Save(new Product{Code="TELA",Name="Tela de prueba",CategoryId=2,Unit="Metro",Price=8000m});
    int fabric;
    await using(var db=await factory.CreateDbContextAsync())fabric=(await db.Products.SingleAsync()).Id;
    await catalog.Adjust(fabric,10,"Ingreso inicial de prueba");
    await Reject(()=>new Catalog(factory,seller).Adjust(fabric,1,"No autorizado"),"Vendedor no puede ajustar stock");
    await Reject(()=>sales.Confirm(new Checkout(Guid.NewGuid(),[new(fabric,1,8000)],0,PaymentMethod.Efectivo)),"Venta sin caja abierta rechazada");
    await commerce.OpenCash(1000);
    await Reject(()=>commerce.OpenCash(0),"Apertura duplicada rechazada");
    var request=new Checkout(Guid.NewGuid(),[new(fabric,1.75m,8000)],0,PaymentMethod.Efectivo);
    var id=await sales.Confirm(request);
    Check(await sales.Confirm(request)==id,"Reintento devuelve la misma venta");
    await using(var db=await factory.CreateDbContextAsync())
    {
        Check(await db.Sales.CountAsync()==1 && await db.Payments.CountAsync()==1,"Un solo pago y cabecera persistidos");
        Check((await db.Products.SingleAsync()).Stock==8.25m,"Stock decimal persistido");
        Check((await db.Sales.Include(x=>x.Lines).SingleAsync()).Lines.Single().Price==8000m,"Precio histórico persistido");
        Check(await db.StockMovements.CountAsync()==2,"Ingreso y venta auditables");
    }
    await Reject(()=>sales.Confirm(new Checkout(Guid.NewGuid(),[new(fabric,100,8000)],0,PaymentMethod.Tarjeta)),"Stock insuficiente revierte transacción");
    await Reject(()=>sales.Confirm(new Checkout(Guid.NewGuid(),[new(fabric,1,7000)],0,PaymentMethod.Efectivo)),"Cambio de precio exige revisar el cobro");
    await Reject(()=>sales.Confirm(new Checkout(Guid.NewGuid(),[new(fabric,1,8000)],100,PaymentMethod.Efectivo)),"Descuento sin autorización rechazado");
    await using(var db=await factory.CreateDbContextAsync())Check(await db.Sales.CountAsync()==1,"Rechazos no dejan ventas parciales");
    var repeated=new Checkout(Guid.NewGuid(),[new(fabric,1,8000)],0,PaymentMethod.Transferencia);
    var duplicates=await Task.WhenAll(sales.Confirm(repeated),sales.Confirm(repeated));
    Check(duplicates[0]==duplicates[1],"Confirmaciones simultáneas idempotentes");
    await catalog.Save(new Product{Code="ULTIMO",Name="Último artículo",CategoryId=3,Unit="Unidad",Price=500});
    int last;await using(var db=await factory.CreateDbContextAsync())last=(await db.Products.SingleAsync(x=>x.Code=="ULTIMO")).Id;
    await catalog.Adjust(last,1,"Única unidad");
    async Task<bool> TrySale(){try{await sales.Confirm(new Checkout(Guid.NewGuid(),[new(last,1,500)],0,PaymentMethod.Efectivo));return true;}catch(InvalidOperationException){return false;}}
    var races=await Task.WhenAll(TrySale(),TrySale());Check(races.Count(x=>x)==1,"Dos ventas compiten por la última unidad: solo una cobra");
    await commerce.ManualCash(200,"Ingreso manual de prueba");await commerce.ManualCash(-200,"Egreso manual de prueba");
    await using(var db=await factory.CreateDbContextAsync())Check(await db.CashMovements.CountAsync(x=>x.SaleId==null)==2,"Ingresos y egresos manuales auditables");
    await commerce.CloseCash(15500);
    await using(var db=await factory.CreateDbContextAsync())
    {
        var closed=await db.CashSessions.SingleAsync();Check(closed.Expected==15500 && closed.Difference==0,"Cierre excluye transferencia del efectivo esperado");
    }
    await commerce.OpenCash(20000);
    await Reject(()=>sales.Cancel(id,"Intento sin permisos"),"Anulación requiere administrador");
    await commerce.Cancel(id,"Devolución de prueba");await commerce.Cancel(id,"Reintento de devolución");
    await using(var db=await factory.CreateDbContextAsync())
    {
        Check((await db.Sales.SingleAsync(x=>x.Id==id)).Cancelled,"Venta anulada conserva cabecera");
        Check((await db.Products.SingleAsync(x=>x.Id==fabric)).Stock==9,"Anulación reintegra stock una sola vez");
        Check((await db.Payments.SingleAsync(x=>x.SaleId==id)).Reversed,"Pago marcado como revertido");
        var closed=await db.CashSessions.SingleAsync(x=>x.Closed!=null);Check(closed.Expected==15500,"Anulación posterior no modifica un cierre anterior");
        Check(await db.CashMovements.CountAsync(x=>x.SaleId==id&&x.Amount<0)==1,"Devolución idempotente registrada en caja actual");
        var user=await db.Users.SingleAsync(x=>x.Id=="seller");user.Active=false;await db.SaveChangesAsync();
    }
    await Reject(()=>sales.OpenCash(1),"Cuenta deshabilitada no puede operar desde circuito abierto");
    await using(var db=await factory.CreateDbContextAsync())Check((await db.Database.GetPendingMigrationsAsync()).Count()==0,"Esquema actualizado sin migraciones pendientes");
    await FiscalChecks.Run(factory,actor,Check,Reject);
    int expectedSales,expectedPayments,expectedInvoices;
    await using(var db=await factory.CreateDbContextAsync())
    {
        expectedSales=await db.Sales.CountAsync();expectedPayments=await db.Payments.CountAsync();
        expectedInvoices=await db.Sales.CountAsync(x=>x.FiscalStatus==FiscalService.Authorized);
    }
    string backupFolder;
    await using(var connection=new SqlConnection(cs.ConnectionString))
    {
        await connection.OpenAsync();await using var cmd=connection.CreateCommand();cmd.CommandText="SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath'))";
        backupFolder=(string?)await cmd.ExecuteScalarAsync() ?? throw new Exception("SQL no informó su carpeta de respaldos.");
    }
    var backup=new BackupService(factory,new LocalOptions{ConnectionString=cs.ConnectionString,BackupFolder=backupFolder},NullLogger<BackupService>.Instance);
    var saved=await backup.Run();Check(saved.Success,"BACKUP consistente y RESTORE VERIFYONLY con CHECKSUM");
    var restoreName=cs.InitialCatalog+"_Restore";
    var masterCs=new SqlConnectionStringBuilder(cs.ConnectionString){InitialCatalog="master"};
    var restoredCs=new SqlConnectionStringBuilder(cs.ConnectionString){InitialCatalog=restoreName};
    try
    {
        await using(var connection=new SqlConnection(masterCs.ConnectionString))
        {
            await connection.OpenAsync();
            var moves=new List<string>();
            await using(var cmd=connection.CreateCommand())
            {
                cmd.CommandText=$"SELECT name,type_desc FROM [{cs.InitialCatalog}].sys.database_files";
                await using var reader=await cmd.ExecuteReaderAsync();
                while(await reader.ReadAsync())
                {
                    var logical=reader.GetString(0);var log=reader.GetString(1)=="LOG";
                    moves.Add("MOVE N'"+logical.Replace("'","''")+"' TO N'"+Path.Combine(backupFolder,restoreName+(log?".ldf":".mdf")).Replace("'","''")+"'");
                }
            }
            await using var restore=connection.CreateCommand();restore.CommandTimeout=600;
            restore.CommandText=$"RESTORE DATABASE [{restoreName}] FROM DISK=@path WITH CHECKSUM, "+string.Join(",",moves)+$"; DBCC CHECKDB ([{restoreName}]) WITH NO_INFOMSGS, ALL_ERRORMSGS;";
            restore.Parameters.AddWithValue("@path",saved.FileName);await restore.ExecuteNonQueryAsync();
        }
        await using var restored=new StoreDb(new DbContextOptionsBuilder<StoreDb>().UseSqlServer(restoredCs.ConnectionString).Options);
        Check(await restored.Sales.CountAsync()==expectedSales && await restored.Payments.CountAsync()==expectedPayments,"Restauración real conserva ventas y pagos");
        Check(expectedInvoices>0 && await restored.Sales.CountAsync(x=>x.FiscalStatus==FiscalService.Authorized && x.Cae!=null)==expectedInvoices,"Restauración conserva facturas y CAE");
        Check((await restored.Sales.SingleAsync(x=>x.Id==id)).Cancelled,"Restauración conserva anulaciones e historial");
        Check((await restored.Products.SingleAsync(x=>x.Id==fabric)).Stock==9,"Restauración conserva stock decimal");
    }
    finally
    {
        await using var restored=new StoreDb(new DbContextOptionsBuilder<StoreDb>().UseSqlServer(restoredCs.ConnectionString).Options);await restored.Database.EnsureDeletedAsync();
        if(saved.FileName is not null) { try { File.Delete(saved.FileName); } catch(IOException){} catch(UnauthorizedAccessException){Console.WriteLine("Copia temporal conservada: "+saved.FileName);} }
    }
    Console.WriteLine($"APROBADAS: {assertions} comprobaciones; persistencia, backup y restauración SQL Server real.");
}
finally
{
    // Only the randomly generated test database above can be removed.
    if(!cs.InitialCatalog.StartsWith("CasaRosita_Test_"))throw new Exception("Nombre de base no permitido.");
    await using var db=await factory.CreateDbContextAsync();await db.Database.EnsureDeletedAsync();
}
sealed class Factory(DbContextOptions<StoreDb> options):IDbContextFactory<StoreDb>{public StoreDb CreateDbContext()=>new(options);}
sealed class Auth(string id):AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,id)],"Test"))));
}
