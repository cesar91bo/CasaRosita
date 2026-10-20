using CasaRosita.Data;
using CasaRosita.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
namespace CasaRosita.Components.Pages;
public abstract class StorePage : ComponentBase
{
    [Inject] protected IDbContextFactory<StoreDb> Factory { get; set; } = null!;
    [Inject] protected Actor Actor { get; set; } = null!;
    [Inject] protected ILogger<StorePage> Log { get; set; } = null!;
    protected string? Error;
    protected string? Notice;
    protected bool Busy;
    protected async Task Run(Func<Task> action)
    {
        if(Busy) return;
        Busy=true; Error=null; Notice=null;
        try { await action(); }
        catch(Exception ex)
        {
            Log.LogError(ex,"Falló una operación de la interfaz");
            Error=ex is InvalidOperationException or UnauthorizedAccessException ? ex.Message : "No se pudo completar la operación. Revisá la conexión local y volvé a consultar el estado antes de repetirla.";
        }
        finally { Busy=false; }
    }
}
