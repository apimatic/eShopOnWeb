using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Boots the PublicApi app with the real Maxio billing service replaced by a configurable fake.
/// </summary>
public class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    public FakeSubscriptionBillingService Billing { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services
                         .Where(d => d.ServiceType == typeof(ISubscriptionBillingService))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<ISubscriptionBillingService>(Billing);
        });
    }
}
