using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory so EF Core tooling (migrations) can construct a SQL Server-configured
/// <see cref="CatalogContext"/> without the application's runtime DI, which may use the in-memory
/// provider. Only used by tooling; never at runtime.
/// </summary>
public class CatalogContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Initial Catalog=Microsoft.eShopOnWeb.CatalogDb;Integrated Security=true;")
            .Options;
        return new CatalogContext(options);
    }
}
