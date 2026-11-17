using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

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
