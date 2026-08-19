using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory used only by the EF Core tools (e.g. <c>dotnet ef migrations add</c>). It
/// configures the SQL Server provider so relational migrations can be scaffolded without starting
/// the application host. It has no effect at runtime, where the context is configured by DI.
/// </summary>
public class CatalogContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Integrated Security=true;Initial Catalog=Microsoft.eShopOnWeb.CatalogDb;");
        return new CatalogContext(optionsBuilder.Options);
    }
}
