using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Design-time factory so EF Core tooling (e.g. <c>dotnet ef migrations add</c>) can build
/// <see cref="CatalogContext"/> without starting the application host (and its seeding). The connection
/// string is a placeholder — migrations are generated, not executed, against it.
/// </summary>
public class CatalogContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=eShopOnWeb.Catalog;Trusted_Connection=True;")
            .Options;

        return new CatalogContext(options);
    }
}
