using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory used by the EF Core tools (e.g. <c>dotnet ef migrations add</c>) to create a
/// <see cref="CatalogContext"/> without spinning up the application host. The connection string is a
/// design-time-only default for the SQL Server (LocalDB) provider and is never used at runtime.
/// </summary>
public class CatalogContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Integrated Security=true;Initial Catalog=Microsoft.eShopOnWeb.CatalogDb;");

        return new CatalogContext(optionsBuilder.Options);
    }
}
