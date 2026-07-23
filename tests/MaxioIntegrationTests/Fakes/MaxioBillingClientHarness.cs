using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>
/// Wires a real <see cref="MaxioBillingClient"/> — the actual SDK client and JSON pipeline — over a
/// stubbed transport, so the tests exercise the integration's genuine request/response behaviour.
/// </summary>
internal sealed class MaxioBillingClientHarness : IDisposable
{
    /// <summary>An explicit base URL, so the tests also prove the override is honoured verbatim.</summary>
    public const string BaseUrl = "http://localhost:8080";

    private readonly HttpClient _httpClient;

    private MaxioBillingClientHarness(StubMaxioHandler handler, MaxioSettings settings)
    {
        Handler = handler;
        _httpClient = new HttpClient(handler);

        Client = new MaxioBillingClient(
            _httpClient,
            Options.Create(settings),
            new MaxioCatalogCache(),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }

    public StubMaxioHandler Handler { get; }

    public MaxioBillingClient Client { get; }

    /// <summary>A harness whose catalog routes are already answered from the seeded sandbox payloads.</summary>
    public static MaxioBillingClientHarness WithSeededCatalog(Action<StubMaxioHandler>? configure = null)
    {
        var handler = new StubMaxioHandler()
            .Map(HttpMethod.Get, "/product_families.json", MaxioPayloads.ProductFamilies)
            .Map(HttpMethod.Get, $"/product_families/{MaxioPayloads.FamilyId}/products.json", MaxioPayloads.Products)
            .Map(HttpMethod.Get, $"/product_families/{MaxioPayloads.FamilyId}/components.json", MaxioPayloads.MeteredComponents);

        configure?.Invoke(handler);

        return new MaxioBillingClientHarness(handler, Settings());
    }

    public static MaxioBillingClientHarness With(StubMaxioHandler handler, MaxioSettings? settings = null) =>
        new(handler, settings ?? Settings());

    public static MaxioSettings Settings() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "example-site",
        Environment = "US",
        BaseUrl = BaseUrl,
        ProductFamilyHandle = "eshop-subscribe",
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = "api-call"
    };

    public void Dispose()
    {
        _httpClient.Dispose();
        Handler.Dispose();
    }
}
