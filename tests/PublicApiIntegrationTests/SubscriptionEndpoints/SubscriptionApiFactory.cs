using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Boots the real PublicApi host but swaps the real Maxio-backed <see cref="IMaxioBillingService"/>
/// for an in-memory fake, so subscription-endpoint tests don't need network access or live
/// Maxio sandbox credentials to run.
/// </summary>
public class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    public readonly FakeMaxioBillingService FakeBillingService = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMaxioBillingService>();
            services.AddSingleton<IMaxioBillingService>(FakeBillingService);
        });
    }
}
