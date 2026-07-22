using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>Builds a real <see cref="MaxioBillingClient"/> over a controllable provider.</summary>
public static class BillingClientFixture
{
    public const string ApiKey = "sk-test-0123456789abcdef";
    public const string BaseUrl = "http://localhost:18080";
    public const string UserReference = "demouser@microsoft.com";

    public static MaxioSettings Settings() => new()
    {
        ApiKey = ApiKey,
        Subdomain = "cp-exp-2",
        Environment = "US",
        BaseUrl = BaseUrl,
        ProductFamilyHandle = "eshop-subscribe",
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = "api-call",
        TimeoutSeconds = 5,
        MaxRetries = 2
    };

    public static (MaxioBillingClient Client, TestLogger<MaxioBillingClient> Logger) Create(
        HttpMessageHandler provider, MaxioSettings? settings = null)
    {
        var logger = new TestLogger<MaxioBillingClient>();
        var httpClient = new HttpClient(provider);
        var client = new MaxioBillingClient(httpClient, Options.Create(settings ?? Settings()), logger);

        return (client, logger);
    }

    /// <summary>A provider that answers the catalog reads every flow needs before it does anything else.</summary>
    public static FakeBillingProvider WithCatalog(this FakeBillingProvider provider) => provider
        .Respond(HttpMethod.Get, "/product_families.json", BillingPayloads.ProductFamilies)
        .Respond(HttpMethod.Get, "/product_families/3023074/products.json", BillingPayloads.ProductsForFamily);
}
