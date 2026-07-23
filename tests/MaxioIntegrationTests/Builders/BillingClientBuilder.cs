using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Builders;

/// <summary>
/// Assembles a <see cref="MaxioBillingClient"/> over a stub transport, with the same settings
/// shape the hosts bind at runtime.
/// </summary>
public sealed class BillingClientBuilder
{
    /// <summary>
    /// A deterministic, non-routable target. Because it is an explicit base URL, it also proves
    /// the override path is the one under test rather than the subdomain-derived host.
    /// </summary>
    public const string BaseUrl = "http://localhost:8080";

    public const string ProductFamilyHandle = "eshop-subscribe";
    public const int ProductFamilyId = 3023074;
    public const string MeteredComponentHandle = "api-call";

    private readonly StubHttpMessageHandler _handler = new();
    private readonly MaxioSettings _settings = new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        Environment = "US",
        BaseUrl = BaseUrl,
        ProductFamilyHandle = ProductFamilyHandle,
        ProductFamilyId = ProductFamilyId,
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = MeteredComponentHandle,

        // Off by default so each queued response maps to exactly one expected call; the retry
        // policy itself is covered explicitly in MaxioBillingClientResilienceTests.
        MaxRetries = 0
    };

    public StubHttpMessageHandler Handler => _handler;

    public TestAppLogger<MaxioBillingClient> Logger { get; } = new();

    /// <summary>Overrides a setting before the client is built.</summary>
    public BillingClientBuilder With(Action<MaxioSettings> configure)
    {
        configure(_settings);
        return this;
    }

    /// <summary>Queues a successful JSON response, in call order.</summary>
    public BillingClientBuilder RespondWithJson(string json)
    {
        _handler.RespondWithJson(json);
        return this;
    }

    /// <summary>Queues a failure response, in call order.</summary>
    public BillingClientBuilder Respond(System.Net.HttpStatusCode statusCode, string json)
    {
        _handler.Respond(statusCode, json);
        return this;
    }

    /// <summary>
    /// Queues the product-family lookup the client performs before any catalogue call, so tests
    /// only have to queue the response they actually care about.
    /// </summary>
    public BillingClientBuilder RespondWithProductFamilyLookup() =>
        RespondWithJson(MaxioResponses.ProductFamilyList(ProductFamilyId, ProductFamilyHandle));

    public MaxioBillingClient Build() =>
        new(new HttpClient(_handler), Options.Create(_settings), Logger);
}
