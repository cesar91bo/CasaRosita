using System.Data;
using CasaRosita.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CasaRosita.Services;
public record CartItem(int ProductId, decimal Quantity, decimal ExpectedPrice);
public record Checkout(Guid OperationId, List<CartItem> Items, decimal Discount, PaymentMethod Method, int? CustomerId = null);
public static class Money
{
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static void Quantity(decimal value, string unit)
    {
        if (value <= 0 || value > 1000000 || decimal.Round(value, 3) != value || (unit == "Unidad" && decimal.Truncate(value) != value))
            throw new InvalidOperationException("Ingresá una cantidad válida: unidades enteras o metros con hasta tres decimales.");
    }
}
public sealed class Commerce(IDbContextFactory<StoreDb> factory, Actor actor)
{
    // SQL application lock spans the entire transaction, including commit. It also coordinates separate processes.
    public static async Task<IDbContextTransaction> Begin(StoreDb db)
    {
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=N'CasaRosita.Comercio', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'La caja está ocupada. Volvé a intentar.', 1;");
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }
    public async Task<long> Confirm(Checkout request)
    {
        var user = await actor.Require(request.Discount != 0);
        if (request.OperationId == Guid.Empty) throw new InvalidOperationException("Falta la identificación del cobro.");
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await Begin(db);
        var existing = await db.Sales.SingleOrDefaultAsync(x => x.OperationId == request.OperationId);
        if (existing is not null)
        {
            if (existing.UserId != user) throw new UnauthorizedAccessException();
            return existing.Id;
        }
        if (!Enum.IsDefined(request.Method) || request.Items.Count == 0 || request.Items.Count > 200) throw new InvalidOperationException("Revisá los artículos y el medio de pago.");
        if (request.Discount < 0 || Money.Round(request.Discount) != request.Discount) throw new InvalidOperationException("Descuento inválido.");
        var cash = await db.CashSessions.SingleOrDefaultAsync(x => x.Closed == null) ?? throw new InvalidOperationException("Primero abrí la caja.");
        var settings = await db.Settings.SingleAsync();
        if (request.CustomerId is not null && !await db.Customers.AnyAsync(x => x.Id == request.CustomerId && x.Active)) throw new InvalidOperationException("Cliente no disponible.");
        var sale = new Sale { OperationId = request.OperationId, Created = DateTimeOffset.Now, UserId = user, Method = request.Method, Discount = request.Discount, CashSessionId = cash.Id, CustomerId = request.CustomerId };
        foreach (var item in request.Items.GroupBy(x => x.ProductId).Select(g => new CartItem(g.Key, g.Sum(x => x.Quantity), g.First().ExpectedPrice)))
        {
            // Reject negative rows before grouping so a forged request cannot cancel quantities.
            if (request.Items.Where(x => x.ProductId == item.ProductId).Any(x => x.Quantity <= 0)) throw new InvalidOperationException("Cantidad inválida.");
            var p = await db.Products.Include(x => x.Category).SingleOrDefaultAsync(x => x.Id == item.ProductId && x.Active) ?? throw new InvalidOperationException("Producto no disponible.");
            if (request.Items.Where(x=>x.ProductId==p.Id).Any(x=>x.ExpectedPrice!=p.Price)) throw new InvalidOperationException($"Cambió el precio de {p.Name}. Quitá ese artículo del carrito, volvé a buscarlo y revisá el total antes de cobrar.");
            Money.Quantity(item.Quantity, p.Unit);
            if (p.TrackStock && !settings.AllowNegativeStock && p.Stock < item.Quantity) throw new InvalidOperationException($"Stock insuficiente: {p.Name} ({p.Stock:N3} disponibles).");
            sale.Lines.Add(new SaleLine { ProductId=p.Id, Code=p.Code, Name=$"{p.Name} {p.Size} {p.Color}".Trim(), Category=p.Category.Name, Unit=p.Unit, Quantity=item.Quantity, Price=p.Price, Subtotal=Money.Round(item.Quantity*p.Price), StockTracked=p.TrackStock });
            if (p.TrackStock) p.Stock -= item.Quantity;
        }
        sale.Subtotal = sale.Lines.Sum(x => x.Subtotal);
        sale.Total = sale.Subtotal - sale.Discount;
        if (sale.Total < 0) throw new InvalidOperationException("El descuento supera el total.");
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        db.Payments.Add(new Payment { SaleId=sale.Id, Method=sale.Method, Amount=sale.Total });
        foreach (var line in sale.Lines.Where(x => x.StockTracked)) db.StockMovements.Add(new StockMovement { ProductId=line.ProductId, SaleId=sale.Id, Quantity=-line.Quantity, Reason="Venta", UserId=user, Created=sale.Created });
        db.CashMovements.Add(new CashMovement { CashSessionId=cash.Id, SaleId=sale.Id, Method=sale.Method, Amount=sale.Total, Reason="Venta", UserId=user, Created=sale.Created });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return sale.Id;
    }
    public async Task<Sale?> Operation(Guid operation)
    {
        var user = await actor.Require();
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await Begin(db);
        return await db.Sales.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == operation && x.UserId == user);
    }
    public async Task Cancel(long id, string reason)
    {
        var user = await actor.Require(true);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5) throw new InvalidOperationException("Ingresá un motivo de al menos cinco caracteres.");
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await Begin(db);
        var sale = await db.Sales.Include(x => x.Lines).SingleAsync(x => x.Id == id);
        if (sale.Cancelled) return;
        if (sale.FiscalStatus is FiscalService.Pending or FiscalService.Authorized)
            throw new InvalidOperationException("Esta venta tiene una factura autorizada o pendiente. Recuperá su estado y gestioná la nota de crédito fiscal antes de devolverla; no admite anulación interna.");
        // Refunds belong to the currently open cash session; previous closures are immutable.
        var cash = await db.CashSessions.SingleOrDefaultAsync(x => x.Closed == null) ?? throw new InvalidOperationException("Abrí una caja para registrar la devolución.");
        foreach (var line in sale.Lines.Where(x => x.StockTracked))
        {
            var p = await db.Products.SingleAsync(x => x.Id == line.ProductId);
            p.Stock += line.Quantity;
            db.StockMovements.Add(new StockMovement { ProductId=p.Id, SaleId=id, Quantity=line.Quantity, Reason="Anulación: " + reason.Trim(), UserId=user, Created=DateTimeOffset.Now });
        }
        sale.Cancelled=true; sale.CancellationReason=reason.Trim(); sale.CancelledBy=user; sale.CancelledAt=DateTimeOffset.Now;
        (await db.Payments.SingleAsync(x => x.SaleId == id)).Reversed=true;
        db.CashMovements.Add(new CashMovement { CashSessionId=cash.Id, SaleId=id, Method=sale.Method, Amount=-sale.Total, Reason="Anulación: " + reason.Trim(), UserId=user, Created=DateTimeOffset.Now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task OpenCash(decimal initial)
    {
        var user = await actor.Require();
        if (initial < 0 || Money.Round(initial) != initial) throw new InvalidOperationException("Saldo inicial inválido.");
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await Begin(db);
        if (await db.CashSessions.AnyAsync(x => x.Closed == null)) throw new InvalidOperationException("La caja ya está abierta.");
        db.CashSessions.Add(new CashSession { Initial=initial, Opened=DateTimeOffset.Now, OpenedBy=user });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task ManualCash(decimal amount, string reason)
    {
        var user = await actor.Require();
        if (amount == 0 || Money.Round(amount) != amount || string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Indicá importe y motivo. Para un egreso usá un importe negativo.");
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await Begin(db);
        var cash = await db.CashSessions.SingleOrDefaultAsync(x => x.Closed == null) ?? throw new InvalidOperationException("La caja está cerrada.");
        db.CashMovements.Add(new CashMovement { CashSessionId=cash.Id, Amount=amount, Reason=reason.Trim(), UserId=user, Created=DateTimeOffset.Now, Method=PaymentMethod.Efectivo });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task CloseCash(decimal counted)
    {
        var user = await actor.Require();
        if (counted < 0 || Money.Round(counted) != counted) throw new InvalidOperationException("Efectivo contado inválido.");
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await Begin(db);
        var cash = await db.CashSessions.SingleOrDefaultAsync(x => x.Closed == null) ?? throw new InvalidOperationException("La caja ya está cerrada.");
        cash.Expected = cash.Initial + (await db.CashMovements.Where(x => x.CashSessionId == cash.Id && x.Method == PaymentMethod.Efectivo).SumAsync(x => (decimal?)x.Amount) ?? 0);
        cash.Counted=counted; cash.Difference=counted-cash.Expected; cash.Closed=DateTimeOffset.Now; cash.ClosedBy=user; cash.OpenSlot=null;
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
