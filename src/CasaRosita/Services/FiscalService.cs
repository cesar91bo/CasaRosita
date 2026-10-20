using System.Globalization;
using System.Text;
using System.Text.Json;
using CasaRosita.Data;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace CasaRosita.Services;

public sealed class FiscalService(IDbContextFactory<StoreDb> factory, Actor actor, FiscalOptions options, IArcaClient arca)
{
    public const string Pending = "PendienteARCA", Authorized = "AutorizadaARCA", Rejected = "RechazadaARCA";
    public static FiscalSnapshot Snapshot(Sale sale) => JsonSerializer.Deserialize<FiscalSnapshot>(sale.FiscalCustomerData ?? "")
        ?? throw new InvalidOperationException("Falta el registro fiscal histórico.");

    // A session lock survives commits used to persist the request BEFORE the remote side effect.
    // It coordinates separate processes; Commerce.Begin coordinates reservation versus cancellation.
    public async Task Issue(long saleId, FiscalReceiver receiver)
    {
        await actor.Require(); options.Validate(); FiscalRules.Validate(receiver);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'CasaRosita.ARCA', @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=10000; IF @r<0 THROW 51001, 'La facturación está ocupada. Volvé a consultar.', 1;");
        try
        {
            var sale = await db.Sales.SingleAsync(x=>x.Id==saleId);
            if (sale.FiscalStatus == Authorized) return;
            if (sale.FiscalStatus == Pending) { await RecoverCore(db,sale); return; }
            var pending = await db.Sales.AsNoTracking().FirstOrDefaultAsync(x=>x.FiscalStatus==Pending);
            if (pending is not null) throw new InvalidOperationException($"Recuperá primero la factura pendiente de la venta #{pending.Id}.");
            if (sale.Cancelled || sale.Total <= 0) throw new InvalidOperationException("La venta está anulada o su total no es positivo.");
            var settings = await db.Settings.AsNoTracking().SingleAsync();
            if (settings.Cuit?.Replace("-", "").Trim() != options.Cuit || string.IsNullOrWhiteSpace(settings.LegalName) || string.IsNullOrWhiteSpace(settings.Address))
                throw new InvalidOperationException("Completá razón social, domicilio y CUIT del negocio en Configuración. El CUIT debe coincidir con el fiscal.");
            var number = checked(await arca.LastNumber()+1);
            if (number > 99999999) throw new InvalidOperationException("Se agotó la numeración del punto de venta.");
            if (await db.Sales.AnyAsync(x=>x.FiscalStatus==Authorized && x.FiscalPoint==options.Point && x.FiscalNumber==number))
                throw new InvalidOperationException("Numeración fiscal incompatible con el historial local. Revisá el ambiente y punto de venta.");
            var snapshot = new FiscalSnapshot(options.Production,options.Cuit,options.Point,number,
                DateTime.Now.ToString("yyyyMMdd",CultureInfo.InvariantCulture),sale.Total,receiver,
                settings.LegalName,settings.Address,options.GrossIncome,options.ActivityStart.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
            await using (var tx = await Commerce.Begin(db))
            {
                await db.Entry(sale).ReloadAsync();
                if (sale.Cancelled) throw new InvalidOperationException("La venta fue anulada.");
                sale.FiscalStatus=Pending; sale.FiscalType="C"; sale.FiscalPoint=snapshot.Point; sale.FiscalNumber=snapshot.Number;
                sale.FiscalCustomerData=JsonSerializer.Serialize(snapshot); sale.FiscalError=null; sale.Cae=null; sale.CaeExpiry=null;
                await db.SaveChangesAsync(); await tx.CommitAsync();
            }
            await RecoverCore(db,sale);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("EXEC sys.sp_releaseapplock @Resource=N'CasaRosita.ARCA', @LockOwner='Session';");
            await db.Database.CloseConnectionAsync();
        }
    }
    // Recovery uses the immutable receiver from the persisted request, never current form fields.
    public Task Recover(long saleId) => RecoverWithSnapshot(saleId);
    async Task RecoverWithSnapshot(long saleId)
    {
        await actor.Require();
        await using var db = await factory.CreateDbContextAsync();
        var sale = await db.Sales.AsNoTracking().SingleAsync(x=>x.Id==saleId);
        if (sale.FiscalStatus==Authorized) return;
        if (sale.FiscalStatus!=Pending) throw new InvalidOperationException("Esta venta no tiene una solicitud pendiente.");
        await Issue(saleId,Snapshot(sale).Receiver);
    }
    async Task RecoverCore(StoreDb db, Sale sale)
    {
        try
        {
            var snapshot = Snapshot(sale);
            var authorization = await arca.Consult(snapshot);
            if (authorization is null)
            {
                // Re-submit exactly the persisted number and date, only after an explicit NOT FOUND.
                // An advanced external sequence requires manual reconciliation, never a fresh number.
                if (await arca.LastNumber() != snapshot.Number-1)
                    throw new FiscalProtocolException("La numeración de ARCA cambió. Se requiere conciliación técnica de la solicitud pendiente.");
                authorization = await arca.Authorize(snapshot);
            }
            sale.Cae=authorization.Cae;
            var expiry=DateOnly.ParseExact(authorization.Expiry,"yyyyMMdd",CultureInfo.InvariantCulture);
            sale.CaeExpiry=new DateTimeOffset(expiry.ToDateTime(TimeOnly.MinValue),TimeSpan.Zero);
            sale.FiscalStatus=Authorized; sale.FiscalError=authorization.Observations;
            await db.SaveChangesAsync();
        }
        catch (FiscalRejectedException ex)
        {
            sale.FiscalStatus=Rejected; sale.FiscalError=ex.Message;
            await db.SaveChangesAsync(); throw;
        }
        catch (Exception ex)
        {
            // Never downgrade an uncertain authorization to a definitive rejection.
            // No raw transport/certificate exceptions: these can contain private paths or credentials.
            sale.FiscalStatus=Pending; sale.FiscalError=(ex is FiscalProtocolException ? ex.Message+" " : "") + "Resultado pendiente. Recuperá el estado con el mismo ambiente y punto de venta; no factures esta venta por otro canal.";
            await db.SaveChangesAsync();
            throw new InvalidOperationException(sale.FiscalError);
        }
    }
    public static string QrUrl(Sale sale)
    {
        if (sale.FiscalStatus!=Authorized || sale.Cae is null) throw new InvalidOperationException("El comprobante todavía no está autorizado.");
        var s=Snapshot(sale);
        var payload=JsonSerializer.Serialize(new { ver=1, fecha=DateOnly.ParseExact(s.Date,"yyyyMMdd",CultureInfo.InvariantCulture).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            cuit=long.Parse(s.Cuit,CultureInfo.InvariantCulture), ptoVta=s.Point, tipoCmp=11, nroCmp=s.Number, importe=s.Total, moneda="PES", ctz=1,
            tipoDocRec=s.Receiver.DocumentType, nroDocRec=s.Receiver.DocumentNumber, tipoCodAut="E", codAut=long.Parse(sale.Cae,CultureInfo.InvariantCulture) });
        return "https://www.arca.gob.ar/fe/qr/?p="+Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)));
    }
    public static string QrImage(Sale sale)
    {
        using var data=QRCodeGenerator.GenerateQrCode(QrUrl(sale),QRCodeGenerator.ECCLevel.M);
        using var code=new PngByteQRCode(data);
        return "data:image/png;base64,"+Convert.ToBase64String(code.GetGraphic(4));
    }
    public static string VatName(int condition) => condition switch { 1=>"IVA Responsable Inscripto",4=>"IVA Sujeto Exento",5=>"Consumidor Final",6=>"Responsable Monotributo",_=>"" };
}
