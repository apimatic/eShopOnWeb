using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory used by EF Core tooling (e.g. <c>dotnet ef migrations</c>) to construct a
/// <see cref="CatalogContext"/> without booting the application host or its data seeding. The
/// connection string is a design-time placeholder only — migrations describe schema, they do not
/// connect — and contains no secrets.
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
