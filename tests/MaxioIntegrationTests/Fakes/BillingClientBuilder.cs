using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> over a stubbed transport, configured the way the
/// composition roots configure it.
/// </summary>
public static class BillingClientBuilder
{
    public const string ProductFamilyHandle = "eshop-subscribe";
    public const string DefaultPlanHandle = "eshop-pro";
    public const string AlternatePlanHandle = "basic-plan";
    public const string MeteredComponentHandle = "api-call";
    public const int ProductFamilyId = 3023074;
    public const int MeteredComponentId = 3057195;

    public static MaxioSettings Settings(string? baseUrl = null, string? environment = "US") => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        Environment = environment,
        BaseUrl = baseUrl,
        ProductFamilyHandle = ProductFamilyHandle,
        DefaultProductHandle = DefaultPlanHandle,
        AlternateProductHandle = AlternatePlanHandle,
        MeteredComponentHandle = MeteredComponentHandle
    };

    public static MaxioBillingClient Build(StubHttpMessageHandler handler, MaxioSettings? settings = null)
    {
        var effective = settings ?? Settings();
        var httpClient = new HttpClient(handler);
        return new MaxioBillingClient(httpClient, Options.Create(effective));
    }

    /// <summary>
    /// Stubs the catalog lookups the client performs before it will record usage: the product
    /// family handle resolving to an id, and the component proving itself metered.
    /// </summary>
    public static StubHttpMessageHandler WithMeteredComponent(this StubHttpMessageHandler handler,
        string kind = "metered_component",
        string unitPrice = "0.01") =>
        handler
            .RespondOk(HttpMethod.Get, "/product_families.json",
                MaxioJson.ProductFamilies((ProductFamilyId, ProductFamilyHandle)))
            .RespondOk(HttpMethod.Get, $"/components/handle:{MeteredComponentHandle}",
                MaxioJson.ComponentResponse(MeteredComponentId, MeteredComponentHandle, kind, unitPrice));
}
