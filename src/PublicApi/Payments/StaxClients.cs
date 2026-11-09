using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The Stax SDK clients, built from the <c>Stax:</c> configuration section
/// (<c>Stax:ApiKey</c>, <c>Stax:PlatformApiKey</c>, <c>Stax:BaseUrl</c>).
/// </summary>
public sealed class StaxClients
{
    public const string SECTION = "Stax";

    /// <summary>Client acting as the shop's own merchant.</summary>
    public StaxFattMerchantApiClient? Merchant { get; }

    /// <summary>Client acting as the platform; null when the shop has no platform key.</summary>
    public StaxFattMerchantApiClient? Platform { get; }

    public StaxResponseCapture Capture { get; } = new();

    public StaxClients(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        var apiKey = Read(configuration, "ApiKey", "STAX_API_KEY");
        var platformKey = Read(configuration, "PlatformApiKey", "STAX_PLATFORM_API_KEY");
        var baseUrl = Read(configuration, "BaseUrl", null);

        Merchant = apiKey is null ? null : Create(apiKey, baseUrl, httpClientFactory);
        Platform = platformKey is null ? null : Create(platformKey, baseUrl, httpClientFactory);
    }

    private static string? Read(IConfiguration configuration, string key, string? envFallback)
    {
        var value = configuration[$"{SECTION}:{key}"];
        if (string.IsNullOrWhiteSpace(value) && envFallback != null)
        {
            value = Environment.GetEnvironmentVariable(envFallback);
        }
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private StaxFattMerchantApiClient Create(string key, string? baseUrl, IHttpClientFactory httpClientFactory)
    {
        var options = new StaxFattMerchantApiClientOptions
        {
            BearerAuth = key,
            Hooks = [Capture.Hook],
            // Never silently retry a payment-affecting call.
            Retry = RetryOptions.Default() with { MaxRetries = 1 },
        };
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            // used verbatim as the API's base address
            options.Server.Default.Production.BaseUrl = baseUrl;
        }
        return new StaxFattMerchantApiClient(httpClientFactory.CreateClient(), options);
    }
}
