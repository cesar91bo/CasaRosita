using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using CasaRosita.Components;
using CasaRosita.Data;
using CasaRosita.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("es-AR");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("es-AR");
var builder = WebApplication.CreateBuilder(args.Where(x=>x!="--migrate").ToArray());
var dataRoot = builder.Configuration["DataRoot"] ?? Environment.GetEnvironmentVariable("CASAROSITA_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CasaRosita");
dataRoot = Path.GetFullPath(dataRoot, builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataRoot);
builder.Configuration.AddJsonFile(Path.Combine(dataRoot, "local.json"), optional:true, reloadOnChange:false);
var options = builder.Configuration.GetSection("Local").Get<LocalOptions>() ?? new();
if (options.Port < 1024 || options.Port > 65535 || options.BackupRetention < 2 || options.BackupRetention > 365 || options.BackupHour < 0 || options.BackupHour > 23) throw new InvalidOperationException("Configuración local inválida.");
var connection = new SqlConnectionStringBuilder(options.ConnectionString);
if (!(connection.DataSource.StartsWith(@".\") || connection.DataSource.StartsWith(@"localhost\", StringComparison.OrdinalIgnoreCase) || connection.DataSource is "." or "localhost" or "127.0.0.1")) throw new InvalidOperationException("Se requiere SQL Server local.");
builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, options.Port));
builder.Services.AddWindowsService(o => o.ServiceName="CasaRosita");
builder.Logging.AddProvider(new FileLogProvider(Path.Combine(dataRoot, "Logs")));
builder.Services.AddSingleton(options);
var fiscalOptions = builder.Configuration.GetSection("Fiscal").Get<FiscalOptions>() ?? new();
builder.Services.AddSingleton(fiscalOptions);
builder.Services.AddHttpClient<IArcaClient, ArcaClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<FiscalService>();
builder.Services.AddSingleton(new DataLocation(dataRoot));
var protection=builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataRoot,"Keys"))).SetApplicationName("CasaRosita");
if(OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine:true);
builder.Services.AddDbContextFactory<StoreDb>(o => o.UseSqlServer(options.ConnectionString));
builder.Services.AddIdentityCore<LocalUser>(o =>
{
    o.Password.RequiredLength=10; o.Password.RequireNonAlphanumeric=false;
    o.Lockout.MaxFailedAccessAttempts=5; o.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(10);
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<StoreDb>().AddSignInManager();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme, o =>
{
    o.LoginPath="/ingresar"; o.AccessDeniedPath="/ingresar"; o.Cookie.Name="CasaRosita.Sesion";
    o.Cookie.HttpOnly=true; o.Cookie.SameSite=SameSiteMode.Strict; o.ExpireTimeSpan=TimeSpan.FromHours(8); o.SlidingExpiration=false;
    o.Events.OnValidatePrincipal = async context =>
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<StoreDb>();
        var id=context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!await db.Users.AnyAsync(x=>x.Id==id && x.Active)) context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization(); builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<Actor>(); builder.Services.AddScoped<Commerce>(); builder.Services.AddScoped<Catalog>(); builder.Services.AddScoped<Administration>();
builder.Services.AddSingleton<BackupService>(); builder.Services.AddHostedService<BackupWorker>();
var app = builder.Build();
if (args.Contains("--migrate"))
{
    using var scope=app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<StoreDb>().Database.MigrateAsync();
    return;
}
try
{
    using var scope=app.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<StoreDb>();
    if (!await db.Database.CanConnectAsync() || (await db.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("SQL Server no está disponible o hay migraciones pendientes. Ejecutá la instalación/actualización.");
    if (!await db.Users.AnyAsync() && !File.Exists(Path.Combine(dataRoot,"primer-inicio.key")))
        await File.WriteAllTextAsync(Path.Combine(dataRoot,"primer-inicio.key"), Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
}
catch (Exception ex) { app.Logger.LogCritical(ex,"No se pudo iniciar Casa Rosita. Revisá SQL Server y la instalación."); throw; }
app.UseExceptionHandler("/Error",createScopeForErrors:true);
app.Use(async (http,next)=>
{
    try { await next(); }
    catch(AntiforgeryValidationException) { http.Response.StatusCode=400; await http.Response.WriteAsync("El formulario venció. Volvé a abrir la página e intentá nuevamente."); }
});
app.UseAuthentication(); app.UseAuthorization(); app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/salud", async (StoreDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { estado="Disponible", version="0.1.0" }) : Results.StatusCode(503));
app.MapPost("/sesion", async (HttpContext http, IAntiforgery anti, SignInManager<LocalUser> signIn, UserManager<LocalUser> users) =>
{
    await anti.ValidateRequestAsync(http); var form=await http.Request.ReadFormAsync();
    var user=await users.FindByNameAsync(form["usuario"].ToString());
    var result=user is { Active:true } ? await signIn.PasswordSignInAsync(user, form["clave"].ToString(),false,true) : Microsoft.AspNetCore.Identity.SignInResult.Failed;
    return Results.LocalRedirect(result.Succeeded ? "/venta" : "/ingresar?error=1");
});
app.MapPost("/salir", async (HttpContext http, IAntiforgery anti) => { await anti.ValidateRequestAsync(http); await http.SignOutAsync(IdentityConstants.ApplicationScheme); return Results.LocalRedirect("/ingresar"); }).RequireAuthorization();
app.MapPost("/crear-administrador", async (HttpContext http, IAntiforgery anti, StoreDb db, UserManager<LocalUser> users, RoleManager<IdentityRole> roles) =>
{
    await anti.ValidateRequestAsync(http); var form=await http.Request.ReadFormAsync();
    var keyPath=Path.Combine(dataRoot,"primer-inicio.key");
    var expectedKey=File.Exists(keyPath) ? (await File.ReadAllTextAsync(keyPath)).Trim() : "";
    if (expectedKey.Length==0 || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(form["codigo"].ToString().Trim()),System.Text.Encoding.UTF8.GetBytes(expectedKey))) return Results.LocalRedirect("/primer-inicio?error=1");
    await using var tx=await Commerce.Begin(db);
    if (await db.Users.AnyAsync()) return Results.LocalRedirect("/ingresar");
    foreach (var role in new[]{"Administrador","Vendedor"})
        if (!await roles.RoleExistsAsync(role)) { var r=await roles.CreateAsync(new IdentityRole(role)); if(!r.Succeeded) throw new InvalidOperationException("No se pudo crear el rol."); }
    var user=new LocalUser { UserName=form["usuario"].ToString().Trim(), LockoutEnabled=true };
    var created=await users.CreateAsync(user,form["clave"].ToString());
    if (!created.Succeeded) return Results.LocalRedirect("/primer-inicio?error=1");
    var assigned=await users.AddToRoleAsync(user,"Administrador");
    if (!assigned.Succeeded) throw new InvalidOperationException("No se pudo asignar el rol.");
    await tx.CommitAsync(); File.Delete(keyPath);
    return Results.LocalRedirect("/ingresar?creado=1");
});
app.MapGet("/respaldo/{id:long}", async (long id, HttpContext http, StoreDb db) =>
{
    if (!await EndpointAccess.Admin(http,db)) return Results.Forbid();
    var record=await db.Backups.SingleOrDefaultAsync(x=>x.Id==id && x.Success);
    if (record?.FileName is null || !File.Exists(record.FileName)) return Results.NotFound();
    return Results.File(record.FileName,"application/octet-stream",Path.GetFileName(record.FileName));
}).RequireAuthorization();
app.MapGet("/informes.csv", Reports.Export).RequireAuthorization();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();
public record DataLocation(string Root);
public partial class Program;
