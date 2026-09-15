using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory so EF Core tooling (e.g. <c>dotnet ef migrations add</c>) can construct the
/// <see cref="CatalogContext"/> for the SQL Server provider without booting the web host. The
/// connection string here is only used at design time to scaffold migrations; it is a local
/// developer default and holds no secrets.
/// </summary>
public class CatalogContextDesignFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Integrated Security=true;Initial Catalog=Microsoft.eShopOnWeb.CatalogDb;")
            .Options;
        return new CatalogContext(options);
    }
}
