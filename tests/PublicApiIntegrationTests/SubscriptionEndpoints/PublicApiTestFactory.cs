using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// PublicApi with the in-memory database, fake (non-secret) Maxio settings, and the Maxio SDK's HttpClient
/// pointed at <see cref="FakeMaxio"/> so no test traffic leaves the process.
/// </summary>
public class PublicApiTestFactory : WebApplicationFactory<Program>
{
    public FakeMaxio Maxio { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("UseOnlyInMemoryDatabase", "true");
        foreach (var (key, value) in BillingTestHost.FakeMaxioSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
            services.AddHttpClient("MaxioAdvancedBilling").ConfigurePrimaryHttpMessageHandler(() => Maxio));
    }
}
