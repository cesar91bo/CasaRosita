using System.Globalization;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;

namespace CasaRosita.Services;

public interface IArcaClient
{
    Task<long> LastNumber();
    Task<FiscalAuthorization?> Consult(FiscalSnapshot invoice);
    Task<FiscalAuthorization> Authorize(FiscalSnapshot invoice);
}

// Fixed HTTPS endpoints. SOAP bodies, tickets and CMS signatures must never be logged.
public sealed class ArcaClient(HttpClient http, FiscalOptions options, DataLocation location, IDataProtectionProvider protection) : IArcaClient
{
    static readonly XNamespace Ns = "http://ar.gov.afip.dif.FEV1/";
    readonly SemaphoreSlim gate = new(1,1);
    record Ticket(string Token, string Sign, DateTimeOffset Expires);
    static XElement E(string name, object value) => new(Ns + name, value);
    static string Value(XContainer node, string name) => node.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value ?? "";
    static XDocument Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 2_000_000, XmlResolver = null });
        return XDocument.Load(reader);
    }
    async Task<XDocument> Soap(string endpoint, string action, XElement body)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        var envelope = new XDocument(new XElement(soap+"Envelope", new XElement(soap+"Body", body)));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("SOAPAction", "\"" + action + "\"");
        request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new FiscalProtocolException($"ARCA devolvió HTTP {(int)response.StatusCode}. Consultá el estado antes de reintentar.");
        var xml = Parse(await response.Content.ReadAsStringAsync());
        if (xml.Descendants(soap+"Fault").Any()) throw new FiscalProtocolException("ARCA informó un error SOAP. Revisá certificado, autorización wsfe y reloj de Windows.");
        return xml;
    }
    async Task<Ticket> Credentials()
    {
        options.Validate();
        await gate.WaitAsync();
        try
        {
            var cache = Path.Combine(location.Root, $"wsaa-{options.Production}-{options.Cuit}.ticket");
            var protector = protection.CreateProtector("CasaRosita.WSAA.v1");
            if (File.Exists(cache))
            {
                try { var t = JsonSerializer.Deserialize<Ticket>(protector.Unprotect(await File.ReadAllTextAsync(cache))); if (t?.Expires > DateTimeOffset.UtcNow.AddMinutes(5)) return t; }
                catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException) { /* Renew invalid cache. */ }
            }
            if (!Path.IsPathFullyQualified(options.CertificatePath) || !Path.IsPathFullyQualified(options.PasswordFile))
                throw new FiscalProtocolException("Configurá rutas absolutas para el PFX y su archivo de contraseña.");
            var password = (await File.ReadAllTextAsync(options.PasswordFile)).TrimEnd('\r','\n');
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath, password, X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                throw new FiscalProtocolException("El certificado fiscal no tiene clave privada o está fuera de vigencia.");
            var now = DateTimeOffset.UtcNow;
            var tra = new XElement("loginTicketRequest", new XAttribute("version","1.0"),
                new XElement("header", new XElement("uniqueId", now.ToUnixTimeSeconds()), new XElement("generationTime", now.AddMinutes(-10).ToString("O")), new XElement("expirationTime", now.AddHours(10).ToString("O"))), new XElement("service","wsfe"));
            var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(tra.ToString(SaveOptions.DisableFormatting))), false);
            cms.ComputeSignature(new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate));
            XNamespace login = "http://wsaa.view.sua.dvadac.desein.afip.gov";
            var result = await Soap(options.Production ? "https://wsaa.afip.gov.ar/ws/services/LoginCms" : "https://wsaahomo.afip.gov.ar/ws/services/LoginCms", "loginCms",
                new XElement(login+"loginCms", new XElement("in0",Convert.ToBase64String(cms.Encode()))));
            var ticketXml = Parse(Value(result,"loginCmsReturn"));
            var ticket = new Ticket(Value(ticketXml,"token"), Value(ticketXml,"sign"), DateTimeOffset.Parse(Value(ticketXml,"expirationTime"),CultureInfo.InvariantCulture));
            if (ticket.Token.Length == 0 || ticket.Sign.Length == 0) throw new FiscalProtocolException("WSAA no devolvió credenciales válidas.");
            await File.WriteAllTextAsync(cache, protector.Protect(JsonSerializer.Serialize(ticket)));
            return ticket;
        }
        finally { gate.Release(); }
    }
    async Task<XElement> Call(string method, params XElement[] parameters)
    {
        var t = await Credentials();
        var xml = await Soap(options.Production ? "https://servicios1.afip.gov.ar/wsfev1/service.asmx" : "https://wswhomo.afip.gov.ar/wsfev1/service.asmx", Ns.NamespaceName + method,
            new XElement(Ns+method, E("Auth",new[]{E("Token",t.Token),E("Sign",t.Sign),E("Cuit",options.Cuit)}),parameters));
        return xml.Descendants(Ns+(method+"Result")).SingleOrDefault() ?? throw new FiscalProtocolException("Respuesta ARCA incompleta.");
    }
    static void Errors(XElement result)
    {
        var errors = result.Descendants(Ns+"Err").ToArray();
        if (errors.Length > 0) throw new FiscalProtocolException("ARCA: " + string.Join("; ", errors.Select(x => Value(x,"Code") + " " + Value(x,"Msg"))));
    }
    public async Task<long> LastNumber()
    {
        var r = await Call("FECompUltimoAutorizado",E("PtoVta",options.Point),E("CbteTipo",11));
        Errors(r);
        return long.Parse(Value(r,"CbteNro"),CultureInfo.InvariantCulture);
    }
    public async Task<FiscalAuthorization?> Consult(FiscalSnapshot s)
    {
        MatchEnvironment(s);
        var r = await Call("FECompConsultar", E("FeCompConsReq",new[]{E("CbteTipo",11),E("CbteNro",s.Number),E("PtoVta",s.Point)}));
        var errors = r.Descendants(Ns+"Err").ToArray();
        if (errors.Length == 1 && Value(errors[0],"Code") == "602" && !r.Elements(Ns+"ResultGet").Any()) return null;
        Errors(r);
        var data = r.Element(Ns+"ResultGet") ?? throw new FiscalProtocolException("Consulta ARCA incompleta.");
        if (Value(data,"Resultado") != "A" || Value(data,"EmisionTipo") != "CAE" || Value(data,"CbteFch") != s.Date ||
            Value(data,"Concepto") != "1" || Value(data,"DocTipo") != s.Receiver.DocumentType.ToString(CultureInfo.InvariantCulture) ||
            Value(data,"DocNro") != s.Receiver.DocumentNumber.ToString(CultureInfo.InvariantCulture) || Value(data,"CbteDesde") != s.Number.ToString(CultureInfo.InvariantCulture) ||
            Value(data,"CbteHasta") != s.Number.ToString(CultureInfo.InvariantCulture) || Value(data,"PtoVta") != s.Point.ToString(CultureInfo.InvariantCulture) || Value(data,"CbteTipo") != "11" ||
            Value(data,"MonId") != "PES" || decimal.Parse(Value(data,"MonCotiz"),CultureInfo.InvariantCulture) != 1 ||
            decimal.Parse(Value(data,"ImpTotal"),CultureInfo.InvariantCulture) != s.Total)
            throw new FiscalProtocolException("El comprobante de ARCA no coincide con esta venta. Se bloqueó la emisión; requiere conciliación técnica.");
        return CheckedAuthorization(Value(data,"CodAutorizacion"),Value(data,"FchVto"),Observations(data));
    }
    void MatchEnvironment(FiscalSnapshot s)
    {
        if (s.Production != options.Production || s.Cuit != options.Cuit || s.Point != options.Point)
            throw new FiscalProtocolException("La configuración fiscal cambió. Restaurá el ambiente, CUIT y punto de venta del comprobante pendiente para recuperarlo.");
    }
    static string? Observations(XElement r) => string.Join("; ", r.Descendants(Ns+"Obs").Select(x=>Value(x,"Code")+" "+Value(x,"Msg")));
    static FiscalAuthorization CheckedAuthorization(string cae, string expiry, string? observations)
    {
        if (cae.Length != 14 || cae.Any(c=>c is < '0' or > '9') || !DateOnly.TryParseExact(expiry,"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))
            throw new FiscalProtocolException("ARCA devolvió un CAE o vencimiento inválido. Recuperá el estado.");
        return new(cae,expiry,observations);
    }
    public async Task<FiscalAuthorization> Authorize(FiscalSnapshot s)
    {
        MatchEnvironment(s);
        var conditions = await Call("FEParamGetCondicionIvaReceptor",E("ClaseCmp","C"));
        Errors(conditions);
        if (!conditions.Descendants(Ns+"CondicionIvaReceptor").Any(x=>Value(x,"Id")==s.Receiver.VatCondition.ToString(CultureInfo.InvariantCulture)))
            throw new FiscalProtocolException("ARCA no admite esa condición de IVA para factura C.");
        var r = await Call("FECAESolicitar", E("FeCAEReq", new[]{
            E("FeCabReq",new[]{E("CantReg",1),E("PtoVta",s.Point),E("CbteTipo",11)}),
            E("FeDetReq",E("FECAEDetRequest",new[]{E("Concepto",1),E("DocTipo",s.Receiver.DocumentType),E("DocNro",s.Receiver.DocumentNumber),
                E("CbteDesde",s.Number),E("CbteHasta",s.Number),E("CbteFch",s.Date),E("ImpTotal",s.Total),E("ImpTotConc",0),E("ImpNeto",s.Total),
                E("ImpOpEx",0),E("ImpTrib",0),E("ImpIVA",0),E("MonId","PES"),E("MonCotiz",1),E("CondicionIVAReceptorId",s.Receiver.VatCondition)}))}));
        // Only an explicit detail rejection proves this attempt was rejected; infrastructure errors remain uncertain.
        var detail = r.Descendants(Ns+"FECAEDetResponse").SingleOrDefault();
        if (detail is not null && Value(detail,"Resultado") == "R") throw new FiscalRejectedException("ARCA rechazó la factura: " + Observations(detail));
        Errors(r);
        if (detail is null || Value(detail,"Resultado") != "A" || Value(detail,"CbteDesde") != s.Number.ToString(CultureInfo.InvariantCulture))
            throw new FiscalProtocolException("Respuesta de autorización incompleta. Recuperá el estado.");
        return CheckedAuthorization(Value(detail,"CAE"),Value(detail,"CAEFchVto"),Observations(detail));
    }
}

