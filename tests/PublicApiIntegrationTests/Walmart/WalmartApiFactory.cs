using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.Walmart;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.Walmart;

/// <summary>
/// A test host that replaces <see cref="IWalmartInventoryService"/> with a
/// <see cref="FakeWalmartInventoryService"/>, so endpoint tests exercise the full HTTP pipeline
/// (routing, auth, serialization) without a live Walmart.
/// </summary>
internal sealed class WalmartApiFactory : WebApplicationFactory<Program>
{
    public FakeWalmartInventoryService Fake { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWalmartInventoryService>();
            services.AddSingleton<IWalmartInventoryService>(Fake);
        });
    }
}
