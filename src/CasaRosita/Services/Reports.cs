using System.Security.Claims;
using System.Text;
using CasaRosita.Data;
using Microsoft.EntityFrameworkCore;
namespace CasaRosita.Services;
public static class EndpointAccess
{
    public static async Task<bool> Admin(HttpContext http, StoreDb db)
    {
        var id=http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return await db.Users.AnyAsync(x=>x.Id==id && x.Active) && await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId==id && r.Name=="Administrador" select ur).AnyAsync();
    }
}
public static class Reports
{
    public static string Cell(string value)
    {
        // Protect spreadsheet users against formula injection.
        if(value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value="'"+value;
        return "\""+value.Replace("\"","\"\"")+"\"";
    }
    public static async Task<IResult> Export(HttpContext http, StoreDb db, string? type, DateTime? from, DateTime? to)
    {
        if(!await EndpointAccess.Admin(http,db)) return Results.Forbid();
        var start=new DateTimeOffset((from ?? DateTime.Today).Date); var end=new DateTimeOffset((to ?? DateTime.Today).Date.AddDays(1));
        var rows=new List<string[]>();
        if(type=="stock")
        {
            rows.Add(["Código","Nombre","Unidad","Stock","Mínimo"]);
            foreach(var p in await db.Products.AsNoTracking().OrderBy(x=>x.Name).ToListAsync()) rows.Add([p.Code,p.Name,p.Unit,p.Stock.ToString(),p.Minimum.ToString()]);
        }
        else if(type=="caja")
        {
            rows.Add(["Fecha","Caja","Motivo","Medio","Importe","Usuario"]);
            foreach(var m in await db.CashMovements.AsNoTracking().Where(x=>x.Created>=start && x.Created<end).OrderBy(x=>x.Created).ToListAsync()) rows.Add([m.Created.ToString("g"),m.CashSessionId.ToString(),m.Reason,m.Method.ToString(),m.Amount.ToString(),m.UserId]);
        }
        else
        {
            rows.Add(["Número","Fecha","Estado","Medio","Subtotal","Descuento","Total"]);
            foreach(var s in await db.Sales.AsNoTracking().Where(x=>x.Created>=start && x.Created<end).OrderBy(x=>x.Id).ToListAsync()) rows.Add([s.Id.ToString(),s.Created.ToString("g"),s.Cancelled?"Anulada":"Confirmada",s.Method.ToString(),s.Subtotal.ToString(),s.Discount.ToString(),s.Total.ToString()]);
        }
        var text=string.Join("\r\n",rows.Select(row=>string.Join(";",row.Select(Cell))));
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(),"text/csv; charset=utf-8",$"CasaRosita-{type ?? "ventas"}-{DateTime.Today:yyyyMMdd}.csv");
    }
}
