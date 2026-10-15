using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PublicApiIntegrationTests.Maxio;

/// <summary>
/// Boots the real PublicApi with placeholder Maxio settings and a fake Maxio transport,
/// so no test needs credentials or network access.
/// </summary>
public class MaxioTestFactory : WebApplicationFactory<Program>
{
    public FakeMaxioHandler Maxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "placeholder-not-a-secret",
            ["Maxio:Subdomain"] = "placeholder",
            ["Maxio:ProductFamilyHandle"] = "placeholder-family"
        }));

        builder.ConfigureServices(services =>
        {
            // The startup check would call the real service; the fake transport replaces the network entirely.
            var check = services.Where(d => d.ImplementationType == typeof(MaxioStartupCheck)).ToList();
            foreach (var d in check) services.Remove(d);
            services.AddHttpClient(MaxioServiceCollectionExtensions.HTTP_CLIENT_NAME)
                .ConfigurePrimaryHttpMessageHandler(() => Maxio);
        });
    }

    /// <summary>Forgets every claim so each test starts from a clean local store.</summary>
    public async Task ResetAsync()
    {
        Maxio.Reset();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        db.MaxioClaims.RemoveRange(await db.MaxioClaims.ToListAsync());
        await db.SaveChangesAsync();
    }
}
