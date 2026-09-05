using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

internal static class MaxioTestFactory
{
    public static MaxioSubscriptionBillingService CreateService(FakeMaxioHandler handler, string productFamilyHandle = "eshop-subscribe")
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new System.Uri("https://fake-site.chargify.com/") };
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "fake-api-key",
            Subdomain = "fake-site",
            ProductFamilyHandle = productFamilyHandle
        });

        var client = new MaxioApiClient(httpClient, options);
        return new MaxioSubscriptionBillingService(client, options);
    }
}
