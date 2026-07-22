using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Builds a real <see cref="MaxioBillingClient"/> over a scripted <see cref="MaxioApiStub"/>.
/// Everything under test is production code: the only substitution is the transport.
/// </summary>
public sealed class MaxioTestHarness : IDisposable
{
    /// <summary>The base URL the harness targets, so tests can assert requests actually go there.</summary>
    public const string BaseUrl = "https://maxio-stub.test";

    public const string ApiKey = "test-api-key";
    public const string ProductFamilyHandle = "eshop-subscribe";
    public const string DefaultPlanHandle = "eshop-pro";
    public const string AlternatePlanHandle = "basic-plan";
    public const string MeteredComponentHandle = "api-call";

    private readonly HttpClient _httpClient;

    public MaxioTestHarness(MaxioApiStub stub, MaxioSettings? settings = null)
    {
        Stub = stub;
        Settings = settings ?? CreateSettings();
        _httpClient = new HttpClient(stub);
        Client = new MaxioBillingClient(_httpClient, Options.Create(Settings));
    }

    public MaxioApiStub Stub { get; }

    public MaxioSettings Settings { get; }

    /// <summary>The integration under test, exposed through the seam the application depends on.</summary>
    public IBillingClient Client { get; }

    /// <summary>
    /// Stubs the product-family lookup and that family's product list — the two reads every
    /// plan-handle resolution goes through, because plans are resolved inside the configured
    /// family rather than site-wide.
    /// </summary>
    public static MaxioApiStub StubCatalog(MaxioApiStub stub, params string[] products)
    {
        var payload = products.Length > 0
            ? products
            : new[]
            {
                MaxioJson.Product(id: 7130997, handle: DefaultPlanHandle, name: "Pro Plan", priceInCents: 29_900L),
                MaxioJson.Product(id: 7130998, handle: AlternatePlanHandle, name: "Basic Plan", priceInCents: 2_900L)
            };

        return stub
            .Respond(HttpMethod.Get, MaxioApiStub.PathEndingWith("product_families.json"),
                System.Net.HttpStatusCode.OK, MaxioJson.ProductFamilyList())
            .Respond(HttpMethod.Get, MaxioApiStub.PathContaining("3026730", "products"),
                System.Net.HttpStatusCode.OK, MaxioJson.ProductList(payload));
    }

    public static MaxioSettings CreateSettings() => new()
    {
        ApiKey = ApiKey,
        Subdomain = "cp-exp-3",
        Environment = "US",
        BaseUrl = BaseUrl,
        ProductFamilyHandle = ProductFamilyHandle,
        DefaultProductHandle = DefaultPlanHandle,
        AlternateProductHandle = AlternatePlanHandle,
        MeteredComponentHandle = MeteredComponentHandle
    };

    public void Dispose() => _httpClient.Dispose();
}
