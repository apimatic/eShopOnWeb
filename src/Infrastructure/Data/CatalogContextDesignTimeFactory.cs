using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Used only by `dotnet ef` to create migrations. The application always
/// configures its DbContext through the normal dependency injection path.
/// </summary>
public class CatalogContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=Microsoft.eShopOnWeb.CatalogDb;Trusted_Connection=True;")
            .Options;

        return new CatalogContext(options);
    }
}
