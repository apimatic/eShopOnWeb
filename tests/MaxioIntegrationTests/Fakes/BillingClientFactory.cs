using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Composes the client the way the hosts do — settings-driven base address, the transient-fault
/// handler in the pipeline — but against <see cref="FakeMaxioServer"/> instead of a live site.
/// </summary>
internal static class BillingClientFactory
{
    public const string ApiKey = "test-api-key-9f3c";

    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = ApiKey,
        Subdomain = "apimatic-hackathon",
        Environment = "US",
        BaseUrl = "https://billing.test",
        ProductFamilyHandle = "eshop-subscribe",
        ProductFamilyId = 3023074,
        DefaultProductHandle = "eshop-pro",
        DefaultProductId = 7126957,
        AlternateProductHandle = "basic-plan",
        AlternateProductId = 7126958,
        MeteredComponentHandle = "api-call",
        MeteredComponentId = 3057195,
        MaxRetryAttempts = 3,
        RetryBaseDelayMilliseconds = 1
    };

    public static MaxioBillingClient Create(FakeMaxioServer server,
        Action<MaxioSettings>? configure = null,
        RecordingAppLogger<MaxioBillingClient>? logger = null)
    {
        var settings = DefaultSettings();
        configure?.Invoke(settings);

        var options = Options.Create(settings);
        var httpClient = new HttpClient(WithFaultHandling(server, options))
        {
            BaseAddress = new Uri(settings.ResolveBaseUrl())
        };

        return new MaxioBillingClient(httpClient, options, logger ?? new RecordingAppLogger<MaxioBillingClient>());
    }

    public static HttpMessageHandler WithFaultHandling(HttpMessageHandler server, IOptions<MaxioSettings> options) =>
        new MaxioTransientFaultHandler(options, new RecordingAppLogger<MaxioTransientFaultHandler>())
        {
            InnerHandler = server
        };
}
