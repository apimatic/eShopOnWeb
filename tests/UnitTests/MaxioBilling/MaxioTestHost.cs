using System.Net;
using Microsoft.eShopWeb.MaxioBilling;
using Microsoft.eShopWeb.MaxioBilling.Interfaces;
using Microsoft.eShopWeb.MaxioBilling.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.UnitTests.MaxioBilling;

/// <summary>
/// Builds the real registration (options binding, named HttpClient, single-send guard, caching)
/// with only the primary handler swapped, so the tests exercise the wiring the app actually uses.
/// </summary>
public static class MaxioTestHost
{
    public const string FamilyHandle = "test-family";
    public const string FamilyId = "3023074";
    public const string PlanHandle = "test-pro";

    public static (ISubscriptionBillingService Service, StubHandler Handler) Build(
        MaxioApiFake fake,
        IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-api-key",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = FamilyHandle,
            ["Maxio:DefaultPlanHandle"] = PlanHandle,
            // Keep the tests fast: no real waiting on the retry pipeline.
            ["Maxio:MaxRetries"] = "1",
            ["Maxio:RequestTimeoutSeconds"] = "5",
            ["Maxio:CallBudgetSeconds"] = "10"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var handler = new StubHandler(fake.Respond);

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.None));
        services.AddMaxioBilling(configuration);

        // Applied after AddMaxioBilling so it replaces the primary handler while leaving the
        // single-send guard in the delegating chain.
        services.AddHttpClient(MaxioClientAccessor.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<ISubscriptionBillingService>(), handler);
    }

    public static HttpResponseMessage NotFound(string path) =>
        StubHandler.Json(HttpStatusCode.NotFound,
            """{"errors":["no stub for __PATH__"]}""".Replace("__PATH__", path));
}
