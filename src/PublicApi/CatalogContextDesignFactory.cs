using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi;

// Migration tooling needs only the SQL model, without starting hosts or contacting Maxio.
public sealed class CatalogContextDesignFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseSqlServer("Server=localhost;Database=CatalogDesignOnly;Integrated Security=true;TrustServerCertificate=true").Options);
}
