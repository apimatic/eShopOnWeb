using System;
using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Support;

/// <summary>
/// Builds <see cref="MaxioBillingClient"/> instances two ways: against the real Maxio sandbox (for
/// genuine end-to-end behaviour), and against a stubbed HttpClient (for error/edge paths that are
/// impractical or unsafe to provoke against a live provider). Sandbox credentials come from the
/// same environment variables the app itself is configured from - never hardcoded.
/// </summary>
public static class MaxioBillingClientTestFactory
{
    public static bool HasLiveCredentials =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_API_KEY")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN"));

    public static MaxioSettings LiveSettings() => new()
    {
        ApiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY") ?? string.Empty,
        Subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN") ?? string.Empty,
        Environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? "US",
        BaseUrl = null,
        ProductFamilyHandle = Environment.GetEnvironmentVariable("MAXIO_DEFAULT_PRODUCT_FAMILY") ?? "eshop-subscribe",
        ProductFamilyId = 3023074,
        DefaultProductHandle = "eshop-pro",
        DefaultProductId = 7126957,
        AlternateProductHandle = "basic-plan",
        AlternateProductId = 7126958,
        MeteredComponentHandle = "api-call",
        MeteredComponentId = 3057295,
    };

    public static MaxioBillingClient CreateLive(out FakeAppLogger<MaxioBillingClient> logger)
    {
        logger = new FakeAppLogger<MaxioBillingClient>();
        return new MaxioBillingClient(new HttpClient(), Options.Create(LiveSettings()), new MeteredComponentValidationCache(), logger);
    }

    public static MaxioBillingClient CreateStubbed(HttpStatusCode statusCode, string json, out StubHttpMessageHandler handler, MaxioSettings? settings = null)
    {
        handler = new StubHttpMessageHandler(statusCode, json);
        var httpClient = new HttpClient(handler);
        return new MaxioBillingClient(httpClient, Options.Create(settings ?? LiveSettings()), new MeteredComponentValidationCache(), new FakeAppLogger<MaxioBillingClient>());
    }
}
