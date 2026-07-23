using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> pointed at a scripted stub instead of the live provider.
/// </summary>
internal static class TestClientFactory
{
    /// <summary>The stub host the integration is retargeted at, proving <c>Maxio:BaseUrl</c> is honored.</summary>
    public const string StubBaseUrl = "http://localhost:8080";

    public const string ApiKey = "test-api-key";

    public static MaxioSettings Settings(string? baseUrl = StubBaseUrl, string environment = "US") => new()
    {
        ApiKey = ApiKey,
        Subdomain = "cp-exp-1",
        Environment = environment,
        BaseUrl = baseUrl,
        ProductFamilyHandle = "eshop-subscribe",
        ProductFamilyId = 3026728,
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = "api-call",
        MeteredComponentId = 3062731
    };

    public static (MaxioBillingClient Client, RecordingLogger<MaxioBillingClient> Logger) Create(
        FakeMaxioHandler handler,
        MaxioSettings? settings = null)
    {
        var logger = new RecordingLogger<MaxioBillingClient>();
        var httpClient = new HttpClient(handler);
        var client = new MaxioBillingClient(httpClient, Options.Create(settings ?? Settings()), logger);

        return (client, logger);
    }
}
