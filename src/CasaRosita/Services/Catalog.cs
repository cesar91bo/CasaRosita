using CasaRosita.Data;
using Microsoft.EntityFrameworkCore;

namespace CasaRosita.Services;
public sealed class Catalog(IDbContextFactory<StoreDb> factory, Actor actor)
{
    public async Task Save(Product input)
    {
        await actor.Require(true);
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length>40) throw new InvalidOperationException("Ingresá un código de hasta 40 caracteres.");
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length>160) throw new InvalidOperationException("Ingresá un nombre de hasta 160 caracteres.");
        if(input.Price<0 || Money.Round(input.Price)!=input.Price) throw new InvalidOperationException("El precio debe ser positivo y tener hasta dos decimales.");
        if(input.Cost<0 || input.Minimum<0) throw new InvalidOperationException("El costo y el stock mínimo no pueden ser negativos.");
        if(input.Unit is not ("Unidad" or "Metro")) throw new InvalidOperationException("Elegí Unidad o Metro.");
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await Commerce.Begin(db);
        var p = input.Id == 0 ? new Product() : await db.Products.SingleAsync(x => x.Id == input.Id);
        if (input.Id != 0 && !p.Version.SequenceEqual(input.Version)) throw new InvalidOperationException("El producto cambió. Volvé a consultarlo antes de guardar.");
        if (input.Id != 0 && p.Unit!=input.Unit && (p.Stock!=0 || await db.StockMovements.AnyAsync(x=>x.ProductId==p.Id))) throw new InvalidOperationException("Un producto con movimientos conserva su unidad. Creá otro producto para cambiar de unidad de medida.");
        if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId)) throw new InvalidOperationException("Elegí una categoría.");
        p.Code=input.Code.Trim(); p.Barcode=string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode.Trim(); p.Name=input.Name.Trim(); p.CategoryId=input.CategoryId; p.Price=input.Price; p.Cost=input.Cost; p.Unit=input.Unit; p.Minimum=input.Minimum; p.TrackStock=input.TrackStock; p.Active=input.Active; p.Size=input.Size; p.Color=input.Color;
        if (p.Id == 0) db.Products.Add(p);
        // Stock is deliberately only changed through audited adjustments.
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task Adjust(int id, decimal delta, string reason)
    {
        var user = await actor.Require(true);
        if (delta == 0 || string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Indicá cantidad y motivo del movimiento.");
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await Commerce.Begin(db);
        var p = await db.Products.SingleAsync(x => x.Id == id);
        Money.Quantity(Math.Abs(delta), p.Unit);
        if (p.Stock + delta < 0 && !(await db.Settings.SingleAsync()).AllowNegativeStock) throw new InvalidOperationException("El ajuste deja stock negativo.");
        p.Stock += delta;
        db.StockMovements.Add(new StockMovement { ProductId=id, Quantity=delta, Reason=reason.Trim(), UserId=user, Created=DateTimeOffset.Now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task SaveCustomer(Customer input)
    {
        await actor.Require(true);
        if (string.IsNullOrWhiteSpace(input.Name)) throw new InvalidOperationException("Ingresá el nombre.");
        await using var db = await factory.CreateDbContextAsync();
        if (input.Id == 0) db.Customers.Add(input); else db.Customers.Update(input);
        await db.SaveChangesAsync();
    }
}
