using System.Security.Claims;
using CasaRosita.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CasaRosita.Services;
// Every operation rechecks the database. A revoked account cannot keep using a live circuit.
public sealed class Actor(AuthenticationStateProvider state, IDbContextFactory<StoreDb> factory)
{
    public async Task<string> Require(bool admin = false)
    {
        var user = (await state.GetAuthenticationStateAsync()).User;
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) throw new UnauthorizedAccessException("Iniciá sesión para continuar.");
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Users.AnyAsync(x => x.Id == id && x.Active)) throw new UnauthorizedAccessException("Usuario deshabilitado.");
        if (admin)
        {
            var role = await db.Roles.SingleAsync(x => x.Name == "Administrador");
            if (!await db.UserRoles.AnyAsync(x => x.UserId == id && x.RoleId == role.Id)) throw new UnauthorizedAccessException("Esta operación requiere un administrador.");
        }
        return id;
    }
    public async Task<bool> IsAdmin()
    { try { await Require(true); return true; } catch (UnauthorizedAccessException) { return false; } }
}
