using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.UnitTests.Billing.Maxio;

/// <summary>
/// Builds the real billing service over a stubbed transport, so the tests exercise the production
/// wiring — options binding, the typed client, the retry handler — instead of a hand-built double.
/// </summary>
public static class MaxioBillingTestHost
{
    public const string ProductFamilyHandle = "demo-plans";

    public static ISubscriptionBillingService Build(StubMaxioHandler handler, IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-api-key",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = ProductFamilyHandle,
            // Keep the retry backoff off the wall clock; the policy itself is still exercised.
            ["Maxio:RetryBaseDelay"] = "00:00:00.001"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                if (value is null)
                {
                    settings.Remove(key);
                }
                else
                {
                    settings[key] = value;
                }
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);
        services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider().GetRequiredService<ISubscriptionBillingService>();
    }

    public static SubscriberIdentity Subscriber(string userName = "demouser@microsoft.com") =>
        new(userName, userName);
}
