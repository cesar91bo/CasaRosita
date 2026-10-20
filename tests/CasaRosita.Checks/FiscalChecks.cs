using CasaRosita.Data;
using CasaRosita.Services;
using Microsoft.EntityFrameworkCore;

static class FiscalChecks
{
    public static async Task Run(IDbContextFactory<StoreDb> factory, Actor actor, Action<bool,string> check, Func<Func<Task>,string,Task> reject)
    {
        var options=new FiscalOptions{Enabled=true,Cuit="20123456786",Point=77,GrossIncome="Exento",ActivityStart=new(2020,1,1)};
        check(FiscalRules.ValidCuit(options.Cuit) && !FiscalRules.ValidCuit("20123456789"),"CUIT: dígito verificador fiscal");
        var receiver=new FiscalReceiver("Comprador de prueba","Calle 123",96,30123456,5);
        var commerce=new Commerce(factory,actor);
        int productId;
        await using(var db=await factory.CreateDbContextAsync())
        {
            var shop=await db.Settings.SingleAsync();shop.Cuit=options.Cuit;shop.LegalName="Emisor de prueba";shop.Address="Domicilio histórico";
            var product=new Product{Code="FISCAL-TEST",Name="Artículo fiscal",CategoryId=1,Unit="Unidad",Price=123.45m,TrackStock=false};
            db.Products.Add(product);await db.SaveChangesAsync();productId=product.Id;
        }
        Task<long> NewSale()=>commerce.Confirm(new Checkout(Guid.NewGuid(),[new(productId,1,123.45m)],0,PaymentMethod.Efectivo));
        var fake=new FakeArca();var fiscal=new FiscalService(factory,actor,options,fake);
        var id=await NewSale();fake.LoseResponse=true;
        await reject(()=>fiscal.Issue(id,receiver),"CAE remoto con respuesta perdida deja estado pendiente");
        await using(var db=await factory.CreateDbContextAsync())check((await db.Sales.FindAsync(id))!.FiscalStatus==FiscalService.Pending,"Solicitud persistida antes del envío");
        await reject(()=>commerce.Cancel(id,"Anular pendiente"),"No se anula venta fiscal incierta");
        var other=await NewSale();
        await reject(()=>fiscal.Issue(other,receiver),"Pendiente bloquea nuevas numeraciones");
        await fiscal.Recover(id);await fiscal.Issue(id,receiver);
        check(fake.Requests==1,"Recuperación tras timeout consulta CAE sin duplicar emisión");
        await reject(()=>commerce.Cancel(id,"Anular autorizada"),"Factura autorizada requiere nota de crédito");
        await using(var db=await factory.CreateDbContextAsync())
        {
            var sale=(await db.Sales.FindAsync(id))!;
            check(sale.Cae=="12345678901234" && sale.CaeExpiry.HasValue && sale.FiscalStatus==FiscalService.Authorized,"CAE y vencimiento persistidos");
            check(FiscalService.QrImage(sale).StartsWith("data:image/png;base64,"),"QR generado localmente");
            var shop=await db.Settings.SingleAsync();shop.LegalName="Nombre cambiado";await db.SaveChangesAsync();
            check(FiscalService.Snapshot(sale).LegalName=="Emisor de prueba","Factura conserva identidad histórica del emisor");
        }
        await FiscalRenderChecks.Run(factory,actor,options,fiscal,id,check);
        fake.RejectNext=true;
        await reject(()=>fiscal.Issue(other,receiver),"Rechazo explícito se registra separado de timeout");
        await using(var db=await factory.CreateDbContextAsync())check((await db.Sales.FindAsync(other))!.FiscalStatus==FiscalService.Rejected,"Estado rechazado persistido");
        await Task.WhenAll(fiscal.Issue(other,receiver),fiscal.Issue(other,receiver));
        check(fake.Requests==3,"Dos solicitudes simultáneas producen una sola autorización adicional");
        var third=await NewSale();fake.FailBeforeSend=true;
        await reject(()=>fiscal.Issue(third,receiver),"Falla de red antes de enviar conserva número reservado");
        var reserved=fake.LastSnapshot;
        fake.FailBeforeSend=false;
        await fiscal.Recover(third);
        check(fake.LastSnapshot==reserved,"Reenvío tras NOT FOUND conserva solicitud completa");
        var fourth=await NewSale();fake.FailBeforeSend=true;
        await reject(()=>fiscal.Issue(fourth,receiver),"Nueva solicitud incierta");
        fake.FailBeforeSend=false;fake.Last++;
        await reject(()=>fiscal.Recover(fourth),"Secuencia externa avanzada bloquea reenvío con otro número");
        fake.Last--;
        options.Point++;
        // Fake client also enforces captured environment so recovery cannot cross points.
        fake.ExpectedPoint=options.Point;
        await reject(()=>fiscal.Recover(fourth),"Cambio de punto de venta no mezcla una solicitud pendiente");
        options.Point--;fake.ExpectedPoint=options.Point;
        await fiscal.Recover(fourth);
        var cancelled=await NewSale();await commerce.Cancel(cancelled,"Venta sin factura");
        await reject(()=>fiscal.Issue(cancelled,receiver),"Venta anulada no se factura");
        await reject(async()=>await fiscal.Issue(await NewSale(),receiver with {DocumentNumber=1}),"Documento inválido rechazado antes de contactar ARCA");
        await SoapChecks.Run(options,check,reject);
    }
    sealed class FakeArca : IArcaClient
    {
        public int Requests; public long Last; public bool LoseResponse,RejectNext,FailBeforeSend;public int ExpectedPoint=77;
        public FiscalSnapshot? LastSnapshot;
        readonly Dictionary<long,FiscalAuthorization> issued=[];
        public Task<long> LastNumber()=>Task.FromResult(Last);
        public Task<FiscalAuthorization?> Consult(FiscalSnapshot s)
        {
            if(s.Point!=ExpectedPoint)throw new InvalidOperationException("Ambiente incompatible");
            return Task.FromResult(issued.GetValueOrDefault(s.Number));
        }
        public Task<FiscalAuthorization> Authorize(FiscalSnapshot s)
        {
            LastSnapshot=s;
            if(FailBeforeSend)throw new HttpRequestException("Simulación sin envío");
            Requests++;
            if(RejectNext){RejectNext=false;throw new FiscalRejectedException("Rechazo de prueba");}
            var result=new FiscalAuthorization("12345678901234","20261231");issued[s.Number]=result;Last=s.Number;
            if(LoseResponse){LoseResponse=false;throw new TaskCanceledException("Respuesta perdida después del CAE");}
            return Task.FromResult(result);
        }
    }
}
