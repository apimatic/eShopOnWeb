using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> over a stubbed transport, configured the way the
/// seeded sandbox is (plan.md §1.3).
/// </summary>
public class MaxioClientBuilder
{
    public const string ProductFamilyHandle = "eshop-subscribe";
    public const string DefaultProductHandle = "eshop-pro";
    public const string AlternateProductHandle = "basic-plan";
    public const string MeteredComponentHandle = "api-call";
    public const int ProductFamilyId = 3026729;

    private readonly StubHttpMessageHandler _handler = new();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-api-key",
        Subdomain = "cp-exp-2",
        Environment = "US",
        BaseUrl = "https://maxio.test/",
        ProductFamilyHandle = ProductFamilyHandle,
        DefaultProductHandle = DefaultProductHandle,
        AlternateProductHandle = AlternateProductHandle,
        MeteredComponentHandle = MeteredComponentHandle
    };

    public StubHttpMessageHandler Handler => _handler;

    public MaxioSettings Settings => _settings;

    /// <summary>
    /// Stubs the product-family listing the client uses to turn the configured handle into an id.
    /// </summary>
    public MaxioClientBuilder WithSeededProductFamily()
    {
        _handler.RespondWith(HttpMethod.Get, "product_families.json", System.Net.HttpStatusCode.OK,
            $$"""
              [ { "product_family": { "id": {{ProductFamilyId}}, "name": "eShopSubscribe",
                  "handle": "{{ProductFamilyHandle}}" }
              } ]
              """);
        return this;
    }

    public MaxioBillingClient Build()
    {
        var httpClient = new HttpClient(_handler);
        return new MaxioBillingClient(httpClient, Options.Create(_settings));
    }
}
