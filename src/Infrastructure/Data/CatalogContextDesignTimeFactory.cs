using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory so EF Core tooling (e.g. <c>dotnet ef migrations add</c>) can build a
/// relational <see cref="CatalogContext"/> without running the web host. The connection string is
/// a placeholder — the tooling only needs a relational provider to scaffold migrations, it does
/// not connect. At runtime the real provider/connection is chosen in <see cref="Dependencies"/>.
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
