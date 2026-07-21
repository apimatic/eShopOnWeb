using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;

public static class TestMaxioBillingClientFactory
{
    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        Environment = "US",
        BaseUrl = null,
        ProductFamilyHandle = "eshop-subscribe",
        ProductFamilyId = 3023108,
        DefaultProductHandle = "eshop-pro",
        DefaultProductId = 7127070,
        AlternateProductHandle = "basic-plan",
        AlternateProductId = 7127071,
        MeteredComponentHandle = "api-call",
        MeteredComponentId = 3057295
    };

    public static MaxioBillingClient Create(SequentialStubHandler handler, MaxioSettings? settings = null)
    {
        var httpClient = new HttpClient(handler);
        return new MaxioBillingClient(httpClient, Options.Create(settings ?? DefaultSettings()));
    }
}
