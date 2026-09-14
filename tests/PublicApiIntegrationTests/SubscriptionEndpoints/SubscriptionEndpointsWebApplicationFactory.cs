using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// A dedicated WebApplicationFactory (separate from ProgramTest's shared instance) that swaps
/// the real Maxio HTTP client for a fake, so these tests never make a network call.
/// </summary>
public class SubscriptionEndpointsWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeMaxioBillingService FakeMaxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(IMaxioBillingService));
            services.AddSingleton<IMaxioBillingService>(FakeMaxio);
        });
    }
}
