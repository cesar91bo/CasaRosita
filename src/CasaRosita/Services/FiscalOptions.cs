namespace CasaRosita.Services;

public sealed class FiscalOptions
{
    public bool Enabled { get; set; }
    public bool Production { get; set; }
    public string Cuit { get; set; } = "";
    public int Point { get; set; }
    public string CertificatePath { get; set; } = "";
    public string PasswordFile { get; set; } = "";
    public string GrossIncome { get; set; } = "";
    public DateOnly ActivityStart { get; set; }
    public void Validate()
    {
        if (!Enabled) throw new InvalidOperationException("La facturación ARCA está deshabilitada. Consultá docs/ARCA.md.");
        if (!FiscalRules.ValidCuit(Cuit) || Point is < 1 or > 99998 || string.IsNullOrWhiteSpace(GrossIncome) || ActivityStart == default)
            throw new InvalidOperationException("Completá CUIT, punto de venta, Ingresos Brutos e inicio de actividades en la configuración fiscal.");
    }
}

public record FiscalReceiver(string Name, string Address, int DocumentType, long DocumentNumber, int VatCondition);
public record FiscalSnapshot(bool Production, string Cuit, int Point, long Number, string Date,
    decimal Total, FiscalReceiver Receiver, string LegalName, string Address, string GrossIncome, string ActivityStart);
public record FiscalAuthorization(string Cae, string Expiry, string? Observations = null);
public sealed class FiscalRejectedException(string message) : InvalidOperationException(message);
public sealed class FiscalProtocolException(string message) : InvalidOperationException(message);

public static class FiscalRules
{
    public static bool ValidCuit(string value)
    {
        if (value.Length != 11 || value.Any(c => c is < '0' or > '9')) return false;
        int[] weights = [5,4,3,2,7,6,5,4,3,2];
        var digit = 11 - weights.Select((w,i) => w * (value[i]-'0')).Sum() % 11;
        digit = digit == 11 ? 0 : digit == 10 ? 9 : digit;
        return value[10]-'0' == digit;
    }
    public static void Validate(FiscalReceiver r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Address) || r.Name.Length > 200 || r.Address.Length > 300)
            throw new InvalidOperationException("Completá nombre o razón social y domicilio del receptor.");
        // Identificamos a todos los compradores: no dependemos de un umbral monetario variable.
        if (!(r.DocumentType == 80 && ValidCuit(r.DocumentNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))) &&
            !(r.DocumentType == 96 && r.DocumentNumber is >= 1000000 and <= 99999999 && r.VatCondition == 5))
            throw new InvalidOperationException("Ingresá CUIT válido o DNI (solo consumidor final).");
        if (r.VatCondition is not (1 or 4 or 5 or 6)) throw new InvalidOperationException("Condición de IVA no admitida.");
    }
}
