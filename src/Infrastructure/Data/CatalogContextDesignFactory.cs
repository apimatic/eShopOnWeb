using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory so EF Core migration tooling can construct <see cref="CatalogContext"/>
/// without booting the application host (and its startup seeding). The connection string is a
/// design-time placeholder for the SQL Server provider only; migrations are generated as code
/// and no database connection is made.
/// </summary>
public class CatalogContextDesignFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=Microsoft.eShopOnWeb.CatalogDb;Trusted_Connection=True;")
            .Options;
        return new CatalogContext(options);
    }
}
