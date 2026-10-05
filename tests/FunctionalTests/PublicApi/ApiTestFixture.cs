using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi;

public class TestApiApplication : WebApplicationFactory<AuthenticateEndpoint>
{
    private readonly string _environment = "Testing";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        // PublicApi refuses to start without Maxio settings; these placeholders are never called by these tests.
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "placeholder-not-a-real-key",
            ["Maxio:Subdomain"] = "placeholder-site",
            ["Maxio:ProductFamilyHandle"] = "placeholder-family",
            ["Maxio:BaseUrl"] = "http://127.0.0.1:9/"
        }));

        // Add mock/test services to the builder here
        builder.ConfigureServices(services =>
        {
            services.AddScoped(sp =>
            {
                // Replace SQLite with in-memory database for tests
                return new DbContextOptionsBuilder<CatalogContext>()
                .UseInMemoryDatabase("DbForPublicApi")
                .UseApplicationServiceProvider(sp)
                .Options;
            });
            services.AddScoped(sp =>
            {
                // Replace SQLite with in-memory database for tests
                return new DbContextOptionsBuilder<AppIdentityDbContext>()
                .UseInMemoryDatabase("IdentityDbForPublicApi")
                .UseApplicationServiceProvider(sp)
                .Options;
            });
        });

        return base.CreateHost(builder);
    }
}
