using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace CasaRosita.Data;

public class LocalUser : IdentityUser { public bool Active { get; set; } = true; }
public class Category { public int Id { get; set; } public string Name { get; set; } = ""; }
public class Product
{
    public int Id { get; set; }
    [Required, MaxLength(40)] public string Code { get; set; } = "";
    [MaxLength(60)] public string? Barcode { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Unit { get; set; } = "Unidad";
    public string? Size { get; set; }
    public string? Color { get; set; }
    public decimal Price { get; set; }
    public decimal? Cost { get; set; }
    public decimal Stock { get; set; }
    public decimal Minimum { get; set; }
    public bool TrackStock { get; set; } = true;
    public bool Active { get; set; } = true;
    [Timestamp] public byte[] Version { get; set; } = [];
}
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Document { get; set; }
    public string? Cuit { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool Active { get; set; } = true;
}
public enum PaymentMethod { Efectivo, Transferencia, Tarjeta, MercadoPago, Otro }
public class Sale
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public DateTimeOffset Created { get; set; }
    public string UserId { get; set; } = "";
    public int? CustomerId { get; set; }
    public long CashSessionId { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public PaymentMethod Method { get; set; }
    public bool Cancelled { get; set; }
    public string? CancellationReason { get; set; }
    public string? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string FiscalStatus { get; set; } = "NoFiscal";
    public string? FiscalType { get; set; }
    public long? FiscalNumber { get; set; }
    public int? FiscalPoint { get; set; }
    public string? Cae { get; set; }
    public DateTimeOffset? CaeExpiry { get; set; }
    public string? FiscalCustomerData { get; set; }
    public string? FiscalError { get; set; }
    public List<SaleLine> Lines { get; set; } = [];
}
public class SaleLine
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public int ProductId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public bool StockTracked { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Subtotal { get; set; }
}
public class Payment
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public bool Reversed { get; set; }
}
public class StockMovement
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public long? SaleId { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTimeOffset Created { get; set; }
}
public class CashSession
{
    public long Id { get; set; }
    public DateTimeOffset Opened { get; set; }
    public DateTimeOffset? Closed { get; set; }
    public string OpenedBy { get; set; } = "";
    public string? ClosedBy { get; set; }
    public decimal Initial { get; set; }
    public decimal? Expected { get; set; }
    public decimal? Counted { get; set; }
    public decimal? Difference { get; set; }
    public int? OpenSlot { get; set; } = 1;
}
public class CashMovement
{
    public long Id { get; set; }
    public long CashSessionId { get; set; }
    public long? SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTimeOffset Created { get; set; }
}
public class ShopSettings
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "Casa Rosita";
    public string? LegalName { get; set; }
    public string? Cuit { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string ReceiptMessage { get; set; } = "¡Gracias por tu compra!";
    public string? LogoData { get; set; }
    public bool AllowNegativeStock { get; set; }
    public string Paper { get; set; } = "A4";
}
public class BackupRecord
{
    public long Id { get; set; }
    public DateTimeOffset Created { get; set; }
    public bool Success { get; set; }
    public string? FileName { get; set; }
    public string? Error { get; set; }
}
