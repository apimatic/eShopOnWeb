using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Design-time factory used by `dotnet ef` so migrations can be generated without starting the web host.
/// The connection string is only used to build the relational model; no database is contacted.
/// </summary>
public class CatalogContextFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__CatalogConnection") ??
            "Server=(localdb)\\mssqllocaldb;Database=Microsoft.eShopOnWeb.CatalogDb;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new CatalogContext(options);
    }
}
