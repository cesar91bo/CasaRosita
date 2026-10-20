using CasaRosita.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace CasaRosita.Data;
public sealed class DesignFactory : IDesignTimeDbContextFactory<StoreDb>
{
    public StoreDb CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<StoreDb>().UseSqlServer(new LocalOptions().ConnectionString).Options);
}
