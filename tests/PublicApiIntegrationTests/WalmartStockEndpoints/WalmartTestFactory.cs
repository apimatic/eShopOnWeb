using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace PublicApiIntegrationTests.WalmartStockEndpoints;

/// <summary>
/// A PublicApi test host whose <see cref="IWalmartInventoryService"/> is replaced by a shared
/// in-memory fake, so Walmart endpoint behaviour can be verified without a live Walmart.
/// </summary>
public class WalmartTestFactory : WebApplicationFactory<Program>
{
    public FakeWalmartInventoryService Walmart { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(IWalmartInventoryService)).ToList();
            foreach (var d in existing)
            {
                services.Remove(d);
            }

            services.AddSingleton<IWalmartInventoryService>(Walmart);
        });
    }
}
