using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CasaRosita.Data;
public class StoreDb(DbContextOptions<StoreDb> options) : IdentityDbContext<LocalUser>(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<ShopSettings> Settings => Set<ShopSettings>();
    public DbSet<BackupRecord> Backups => Set<BackupRecord>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            { p.SetPrecision(18); p.SetScale(3); }
        b.Entity<Product>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Product>().HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
        b.Entity<Product>().ToTable(t => { t.HasCheckConstraint("CK_Product_Price", "[Price] >= 0 AND ([Cost] IS NULL OR [Cost] >= 0)"); });
        b.Entity<Sale>().HasIndex(x => x.OperationId).IsUnique();
        b.Entity<Sale>().HasIndex(x => x.Created);
        b.Entity<Sale>().HasOne<CashSession>().WithMany().HasForeignKey(x => x.CashSessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Sale>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Sale>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaleLine>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Payment>().HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Payment>().HasIndex(x => x.SaleId).IsUnique();
        b.Entity<CashSession>().HasIndex(x => x.OpenSlot).IsUnique().HasFilter("[OpenSlot] IS NOT NULL");
        b.Entity<CashMovement>().HasOne<CashSession>().WithMany().HasForeignKey(x => x.CashSessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockMovement>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Category>().HasData(new Category { Id=1, Name="Mercería" }, new Category { Id=2, Name="Telas" }, new Category { Id=3, Name="Kiosco" }, new Category { Id=4, Name="Indumentaria" });
        b.Entity<ShopSettings>().HasData(new ShopSettings());
    }
}
