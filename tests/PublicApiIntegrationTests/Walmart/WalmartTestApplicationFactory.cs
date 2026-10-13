using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Walmart;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.Walmart;

/// <summary>
/// WebApplicationFactory that swaps the real (HTTP) Walmart client for an in-memory fake, so the
/// Walmart flows run end-to-end through PublicApi without reaching a live Walmart environment.
/// </summary>
public class WalmartTestApplicationFactory : WebApplicationFactory<Program>
{
    public FakeWalmartInventoryClient Walmart { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWalmartInventoryClient>();
            services.AddSingleton<IWalmartInventoryClient>(Walmart);
        });
    }
}
