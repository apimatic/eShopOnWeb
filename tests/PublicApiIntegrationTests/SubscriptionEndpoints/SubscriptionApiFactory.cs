using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Hosts the API with the billing gateway swapped for <see cref="StubBillingGateway"/>, so
/// these tests are hermetic and never touch a real Maxio site.
/// </summary>
internal class SubscriptionApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBillingGateway>();
            services.AddSingleton<IBillingGateway, StubBillingGateway>();
        });
    }
}
