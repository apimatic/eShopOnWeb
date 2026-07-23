using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>Builds a <see cref="MaxioBillingClient"/> wired to a fake provider.</summary>
public static class TestBillingClientFactory
{
    public const string BaseUrl = "https://cp-exp-4.chargify.com";

    public static MaxioSettings Settings(Action<MaxioSettings>? customize = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-api-key",
            Subdomain = "cp-exp-4",
            Environment = "US",
            ProductFamilyHandle = "eshop-subscribe",
            DefaultProductHandle = "eshop-pro",
            AlternateProductHandle = "basic-plan",
            MeteredComponentHandle = "api-call",
            PaymentCollectionMethod = "remittance"
        };

        customize?.Invoke(settings);
        return settings;
    }

    public static MaxioBillingClient Create(RecordingHttpMessageHandler handler, MaxioSettings? settings = null)
    {
        settings ??= Settings();

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };

        return new MaxioBillingClient(httpClient, Options.Create(settings), new NullAppLogger<MaxioBillingClient>());
    }
}

/// <summary>Swallows log output; these tests assert behaviour, not logging.</summary>
public sealed class NullAppLogger<T> : IAppLogger<T>
{
    public void LogInformation(string message, params object[] args)
    {
    }

    public void LogWarning(string message, params object[] args)
    {
    }
}
