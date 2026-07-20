using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Builds one real <see cref="MaxioBillingClient"/> (and the underlying SDK client/HttpClient)
/// against the Maxio sandbox, shared across the whole test collection — the SDK client is meant
/// to be constructed once and reused, not per test. Credentials come from the environment only;
/// the non-secret sandbox ids/handles are the live, documented values (plan.md §1.3).
/// </summary>
public class MaxioFixture : IDisposable
{
    private readonly HttpClient _httpClient;

    public MaxioSettings Settings { get; }
    public IBillingClient BillingClient { get; }
    internal MaxioAdvancedBillingClient SdkClient { get; }

    public MaxioFixture()
    {
        var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
        var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
        var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        var productFamilyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY");

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(subdomain))
        {
            throw new InvalidOperationException(
                "MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN must be set in the environment to run the Maxio sandbox integration tests.");
        }

        Settings = new MaxioSettings
        {
            ApiKey = apiKey,
            Subdomain = subdomain,
            Environment = string.IsNullOrWhiteSpace(environment) ? "US" : environment,
            ProductFamilyHandle = string.IsNullOrWhiteSpace(productFamilyHandle) ? "eshop-subscribe" : productFamilyHandle,
            ProductFamilyId = 3023074,
            DefaultProductHandle = "eshop-pro",
            DefaultProductId = 7126957,
            AlternateProductHandle = "basic-plan",
            AlternateProductId = 7126958,
            MeteredComponentHandle = "api-call",
            MeteredComponentId = 3057195
        };

        _httpClient = new HttpClient();
        SdkClient = MaxioClientFactory.Create(_httpClient, Settings);
        BillingClient = new MaxioBillingClient(SdkClient, Options.Create(Settings));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

[CollectionDefinition(Name)]
public class MaxioCollection : ICollectionFixture<MaxioFixture>
{
    public const string Name = "Maxio sandbox";
}
