using System.Net;
using System.Net.Http;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>Builds a <see cref="MaxioBillingClient"/> wired to a fake handler, matching the SDK's documented test seam.</summary>
public static class MaxioBillingClientFactory
{
    public static readonly MaxioSettings DefaultSettings = new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-subdomain",
        Environment = "US",
        ProductFamilyHandle = "eshop-subscribe",
        ProductFamilyId = 3023074,
        DefaultProductHandle = "eshop-pro",
        DefaultProductId = 7126957,
        AlternateProductHandle = "basic-plan",
        AlternateProductId = 7126958,
        MeteredComponentHandle = "api-call",
        MeteredComponentId = 3057195
    };

    public static MaxioBillingClient Create(FakeHttpMessageHandler handler, MaxioSettings? settings = null)
    {
        settings ??= DefaultSettings;
        var options = new MaxioAdvancedBillingClientOptions();

        var httpClient = new HttpClient(handler);
        var sdkClient = new MaxioAdvancedBillingClient(httpClient, options);

        return new MaxioBillingClient(sdkClient, Options.Create(settings), Substitute.For<IAppLogger<MaxioBillingClient>>());
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
