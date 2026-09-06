using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Hosts the real PublicApi with the billing gateway swapped for an in-memory fake, so the
/// subscription endpoints can be tested end to end without a live Maxio site.
/// </summary>
public class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    public FakeBillingGateway Gateway { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBillingGateway>();
            services.AddSingleton<IBillingGateway>(Gateway);
        });
    }
}
