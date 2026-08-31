using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory used by the EF Core tools (e.g. <c>dotnet ef migrations add</c>) so that
/// migrations can be scaffolded against the SQL Server provider without booting the web host.
/// It is never used at runtime.
/// </summary>
public class CatalogContextFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Integrated Security=true;Initial Catalog=Microsoft.eShopOnWeb.CatalogDb;");
        return new CatalogContext(optionsBuilder.Options);
    }
}
