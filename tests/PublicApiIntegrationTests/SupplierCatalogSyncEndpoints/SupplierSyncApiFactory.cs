using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SupplierCatalogSyncEndpoints;

/// <summary>
/// Boots the real PublicApi host (in-memory database) but swaps the Firecrawl-backed scraper
/// for <see cref="FakeSupplierProductScraper"/>, so the whole sync flow is exercised end-to-end
/// without reaching out to Firecrawl.
/// </summary>
public class SupplierSyncApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISupplierProductScraper));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddScoped<ISupplierProductScraper, FakeSupplierProductScraper>();
        });
    }
}
