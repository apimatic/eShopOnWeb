using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory so EF Core tooling (e.g. <c>dotnet ef migrations add</c>) can build
/// <see cref="CatalogContext"/> against the SQL Server provider without starting the web host or
/// touching a live database. Not used at runtime.
/// </summary>
public class DesignTimeCatalogContextFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Integrated Security=true;Initial Catalog=Microsoft.eShopOnWeb.CatalogDb;");
        return new CatalogContext(optionsBuilder.Options);
    }
}
