using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CasaRosita.Services;
using Microsoft.AspNetCore.DataProtection;

static class SoapChecks
{
    public static async Task Run(FiscalOptions options,Action<bool,string> check,Func<Func<Task>,string,Task> reject)
    {
        var root=Path.Combine(Path.GetTempPath(),"CasaRosita_FiscalTest_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var protection=new EphemeralDataProtectionProvider();
            var ticket=JsonSerializer.Serialize(new{Token="test-token",Sign="test-sign",Expires=DateTimeOffset.UtcNow.AddHours(1)});
            await File.WriteAllTextAsync(Path.Combine(root,$"wsaa-{options.Production}-{options.Cuit}.ticket"),protection.CreateProtector("CasaRosita.WSAA.v1").Protect(ticket));
            var handler=new FixtureHandler();using var http=new HttpClient(handler);
            var client=new ArcaClient(http,options,new(root),protection);
            var s=new FiscalSnapshot(false,options.Cuit,77,8,"20261019",123.45m,new("Prueba","Calle 1",96,30123456,5),"Emisor","Calle 2","Exento","2020-01-01");
            check(await client.LastNumber()==7,"SOAP interpreta último número autorizado");
            check(await client.Consult(s) is null,"SOAP 602 permite recuperar número sin comprobante");
            var authorization=await client.Authorize(s);
            check(authorization.Cae=="12345678901234" && handler.ValidRequest,"SOAP C: tipo 11, concepto 1, pesos, IVA cero y decimales invariantes");
            handler.ConsultFound=true;
            check((await client.Consult(s))?.Cae==authorization.Cae,"SOAP consulta recupera CAE y verifica datos");
            await reject(()=>client.Consult(s with {Total=999}),"SOAP no asocia CAE de importe diferente");
            handler.Reject=true;
            await reject(()=>client.Authorize(s),"SOAP interpreta rechazo explícito de detalle");
            handler.Fail=true;
            await reject(()=>client.Consult(s),"SOAP error HTTP no se interpreta como comprobante inexistente");
        }
        finally{Directory.Delete(root,true);}
    }
    sealed class FixtureHandler : HttpMessageHandler
    {
        public bool ValidRequest,ConsultFound,Reject,Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(Fail)return new(HttpStatusCode.InternalServerError);
            XNamespace ns="http://ar.gov.afip.dif.FEV1/",soap="http://schemas.xmlsoap.org/soap/envelope/";
            var body=XDocument.Parse(await request.Content!.ReadAsStringAsync(token)).Descendants(soap+"Body").Single().Elements().Single();
            var method=body.Name.LocalName;
            string V(string name)=>body.Descendants(ns+name).Single().Value;
            string inner=method switch{
                "FECompUltimoAutorizado"=>"<CbteNro>7</CbteNro>",
                "FEParamGetCondicionIvaReceptor"=>"<ResultGet><CondicionIvaReceptor><Id>5</Id><Desc>Consumidor Final</Desc><Cmp_Clase>C</Cmp_Clase></CondicionIvaReceptor></ResultGet>",
                "FECompConsultar" when !ConsultFound=>"<Errors><Err><Code>602</Code><Msg>No existen datos</Msg></Err></Errors>",
                "FECompConsultar"=>"<ResultGet><Concepto>1</Concepto><DocTipo>96</DocTipo><DocNro>30123456</DocNro><CbteDesde>8</CbteDesde><CbteHasta>8</CbteHasta><CbteFch>20261019</CbteFch><ImpTotal>123.45</ImpTotal><MonId>PES</MonId><MonCotiz>1</MonCotiz><Resultado>A</Resultado><CodAutorizacion>12345678901234</CodAutorizacion><EmisionTipo>CAE</EmisionTipo><FchVto>20261029</FchVto><PtoVta>77</PtoVta><CbteTipo>11</CbteTipo></ResultGet>",
                "FECAESolicitar" when Reject=>"<FeDetResp><FECAEDetResponse><Resultado>R</Resultado><Observaciones><Obs><Code>100</Code><Msg>Rechazo de fixture</Msg></Obs></Observaciones></FECAEDetResponse></FeDetResp>",
                "FECAESolicitar"=>"<FeDetResp><FECAEDetResponse><Resultado>A</Resultado><CbteDesde>8</CbteDesde><CAE>12345678901234</CAE><CAEFchVto>20261029</CAEFchVto></FECAEDetResponse></FeDetResp>",
                _=>throw new InvalidOperationException("Método SOAP inesperado")};
            if(method=="FECAESolicitar")
            {
                ValidRequest=V("CbteTipo")=="11" && V("Concepto")=="1" && V("ImpNeto")=="123.45" && V("ImpTotal")=="123.45" && V("ImpIVA")=="0" && V("MonId")=="PES" && V("MonCotiz")=="1" && V("CondicionIVAReceptorId")=="5" && !body.Descendants(ns+"Iva").Any();
            }
            if(V("Token")!="test-token" || V("Sign")!="test-sign" || request.Headers.GetValues("SOAPAction").Single()!="\""+ns.NamespaceName+method+"\"")throw new Exception("Credenciales o acción SOAP mal formadas");
            var response=$"<soap:Envelope xmlns:soap=\"{soap}\"><soap:Body><{method}Response xmlns=\"{ns}\"><{method}Result>{inner}</{method}Result></{method}Response></soap:Body></soap:Envelope>";
            return new(HttpStatusCode.OK){Content=new StringContent(response,Encoding.UTF8,"text/xml")};
        }
    }
}
