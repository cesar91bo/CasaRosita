using CasaRosita.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace CasaRosita.Services;
public sealed class Administration(IDbContextFactory<StoreDb> factory, Actor actor, IServiceScopeFactory scopes)
{
    public async Task SaveSettings(ShopSettings input)
    {
        await actor.Require(true);
        if (string.IsNullOrWhiteSpace(input.Name) || (input.Paper != "A4" && input.Paper != "80mm")) throw new InvalidOperationException("Revisá el nombre y formato de papel.");
        await using var db=await factory.CreateDbContextAsync(); input.Id=1; db.Settings.Update(input); await db.SaveChangesAsync();
    }
    public async Task CreateUser(string name,string password,string role)
    {
        await actor.Require(true);
        if (role is not ("Administrador" or "Vendedor")) throw new InvalidOperationException("Rol inválido.");
        using var scope=scopes.CreateScope(); var manager=scope.ServiceProvider.GetRequiredService<UserManager<LocalUser>>();
        var user=new LocalUser { UserName=name.Trim(), LockoutEnabled=true };
        var result=await manager.CreateAsync(user,password);
        if(!result.Succeeded) throw new InvalidOperationException("No se creó el usuario. Revisá que el nombre sea único y la contraseña tenga 10 caracteres, mayúsculas, minúsculas y números.");
        var assigned=await manager.AddToRoleAsync(user,role);
        if(!assigned.Succeeded) { await manager.DeleteAsync(user); throw new InvalidOperationException("No se pudo asignar el rol."); }
    }
    public async Task SetActive(string id,bool active)
    {
        var current=await actor.Require(true);
        if(id==current) throw new InvalidOperationException("No podés deshabilitar tu propia cuenta.");
        await using var db=await factory.CreateDbContextAsync(); var user=await db.Users.SingleAsync(x=>x.Id==id); user.Active=active; await db.SaveChangesAsync();
    }
}
