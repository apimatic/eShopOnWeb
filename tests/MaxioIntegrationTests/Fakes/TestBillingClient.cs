using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Builds the real <c>MaxioBillingClient</c> over a stubbed transport, configured the way the
/// composition roots configure it against the seeded eShopSubscribe sandbox.
/// </summary>
internal static class TestBillingClient
{
    public const string ApiKey = "test-api-key";
    public const string Subdomain = "cp-exp-2";

    public static MaxioSettings Settings() => new()
    {
        ApiKey = ApiKey,
        Subdomain = Subdomain,
        Environment = MaxioSettings.UnitedStatesRegion,
        ProductFamilyHandle = "eshop-subscribe",
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = "api-call"
    };

    /// <summary>
    /// Creates the client over a stubbed transport. <paramref name="baseAddress"/> is left unset by
    /// default so the client's own configuration-driven resolution is what decides the target host;
    /// pass one to stand in for a composition root that has already set it.
    /// </summary>
    public static MaxioBillingClient Create(StubHttpMessageHandler handler, MaxioSettings? settings = null, Uri? baseAddress = null)
    {
        settings ??= Settings();

        var httpClient = new HttpClient(handler);
        if (baseAddress is not null)
        {
            httpClient.BaseAddress = baseAddress;
        }

        return new MaxioBillingClient(httpClient,
            Options.Create(settings),
            new RecordingAppLogger<MaxioBillingClient>());
    }
}
