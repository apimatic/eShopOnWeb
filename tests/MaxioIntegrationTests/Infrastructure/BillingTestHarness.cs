using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;

/// <summary>
/// Builds a real <see cref="MaxioBillingClient"/> over a scripted transport, wired exactly the way the
/// composition roots wire it, so tests exercise the shipped configuration path and not a bespoke one.
/// </summary>
public static class BillingTestHarness
{
    public const string Subdomain = "cp-exp-2";
    public const string ProductFamilyHandle = "eshop-subscribe";
    public const string MeteredComponentHandle = "api-call";

    public static MaxioSettings Settings(string? baseUrl = null, string region = MaxioRegion.Us) => new()
    {
        ApiKey = "test-api-key",
        Subdomain = Subdomain,
        Environment = region,
        BaseUrl = baseUrl,
        ProductFamilyHandle = ProductFamilyHandle,
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = MeteredComponentHandle
    };

    public static MaxioBillingClient Build(HttpMessageHandler handler, MaxioSettings? settings = null)
    {
        settings ??= Settings();

        var httpClient = new HttpClient(handler);
        var sdkClient = new MaxioAdvancedBillingClient(httpClient, MaxioClientOptionsFactory.Create(settings));

        return new MaxioBillingClient(
            sdkClient,
            new MaxioCatalogCache(TimeSpan.FromMinutes(30)),
            settings,
            new NullAppLogger<MaxioBillingClient>());
    }
}

/// <summary>A logger that records nothing, so tests assert on behaviour rather than on log output.</summary>
public sealed class NullAppLogger<T> : IAppLogger<T>
{
    public void LogInformation(string message, params object[] args)
    {
    }

    public void LogWarning(string message, params object[] args)
    {
    }
}
