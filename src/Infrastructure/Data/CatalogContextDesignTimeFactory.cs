using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory so EF Core tooling can build the model (e.g. to add a migration) against the SQL
/// Server provider without booting the application host. The connection string is a placeholder — adding a
/// migration builds the model only and does not connect.
/// </summary>
public class CatalogContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=eShopOnWeb.CatalogDb;")
            .Options;

        return new CatalogContext(options);
    }
}
