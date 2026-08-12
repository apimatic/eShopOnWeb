using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory so EF Core tooling (migrations) can construct <see cref="CatalogContext"/>
/// without booting the application host. The connection string is a design-time placeholder only —
/// migrations are generated, not applied, so no real database is contacted and no secret is used.
/// </summary>
public class CatalogContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=eShopOnWeb.DesignTime;Trusted_Connection=True;")
            .Options;
        return new CatalogContext(options);
    }
}
