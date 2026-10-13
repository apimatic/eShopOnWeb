using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests;

/// <summary>
/// Boots PublicApi for integration tests with Walmart replaced by an in-memory fake, so every flow
/// is driveable through the HTTP surface without a live Walmart environment.
/// </summary>
public class WalmartApiFactory : WebApplicationFactory<Program>
{
    public FakeWalmartInventoryService Walmart { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWalmartInventoryService>();
            services.AddSingleton<IWalmartInventoryService>(Walmart);
        });
    }
}
