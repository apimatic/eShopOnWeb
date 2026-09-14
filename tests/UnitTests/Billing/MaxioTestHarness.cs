using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Billing;

/// <summary>
/// Test seam for the Maxio integration: the SDK client takes an <see cref="HttpClient"/>, so a stub
/// <see cref="HttpMessageHandler"/> exercises the real SDK — real serialization, real error types, real retry
/// pipeline — without touching the network.
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    /// <summary>Every request, in order. Retries append here, so this is what a write-once assertion counts.</summary>
    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string> Bodies { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responder(request);
    }

    public int CountOf(HttpMethod method, string pathFragment) =>
        Requests.Count(r => r.Method == method &&
                            r.RequestUri!.AbsolutePath.Contains(pathFragment, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Routes a stubbed Maxio call by path. An unmatched path throws with the path in the message, so a wrong
/// assumption about a route shows up as a readable failure rather than a mystery deserialization error.
/// </summary>
public sealed class MaxioRouter
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = new();

    public MaxioRouter Map(
        HttpMethod method,
        string pathFragment,
        HttpStatusCode status,
        string json) =>
        Map(method, pathFragment, _ => Json(status, json));

    public MaxioRouter Map(
        HttpMethod method,
        string pathFragment,
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((r => r.Method == method &&
                          r.RequestUri!.AbsolutePath.Contains(pathFragment, StringComparison.OrdinalIgnoreCase),
                     respond));
        return this;
    }

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        foreach (var (match, respond) in _routes)
        {
            if (match(request))
            {
                return respond(request);
            }
        }

        throw new InvalidOperationException(
            $"No stub route for {request.Method} {request.RequestUri!.AbsolutePath}{request.RequestUri.Query}");
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> over a fixed value.</summary>
public sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Hands the service a client built over the stub handler.</summary>
public sealed class StubClientProvider : IMaxioClientProvider
{
    private readonly MaxioAdvancedBillingClient _client;

    public StubClientProvider(MaxioAdvancedBillingClient client) => _client = client;

    public MaxioAdvancedBillingClient GetClient() => _client;
}

public static class MaxioTestHarness
{
    public const string FamilyHandle = "eshop-subscribe";
    public const string ProPlanHandle = "eshop-pro";
    public const int FamilyId = 3023074;
    public const int CustomerId = 555001;
    public const int SubscriptionId = 94209931;

    public static MaxioSettings Settings(string? baseUrl = null) => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "cp-exp-2",
        ProductFamilyHandle = FamilyHandle,
        BaseUrl = baseUrl
    };

    /// <summary>
    /// Builds the service over the stub, wiring the same write-once handler the application registers so the
    /// duplicate-write guarantee is exercised, not bypassed.
    /// </summary>
    public static (MaxioSubscriptionBillingService Service, StubHandler Handler) CreateService(
        MaxioRouter router,
        MaxioSettings? settings = null)
    {
        var handler = new StubHandler(router.Respond);
        var httpClient = new HttpClient(new MaxioWriteOnceHandler { InnerHandler = handler });
        var client = new MaxioAdvancedBillingClient(httpClient, MaxioClientOptions(settings ?? Settings()));

        var service = new MaxioSubscriptionBillingService(
            new StubClientProvider(client),
            new StaticOptionsMonitor<MaxioSettings>(settings ?? Settings()),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

        return (service, handler);
    }

    private static MaxioAdvancedBillingClientOptions MaxioClientOptions(MaxioSettings settings)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us,
            BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
            {
                Username = settings.ApiKey!,
                Password = "x"
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.Subdomain))
        {
            options.Server.Production.Us.Site = settings.Subdomain;
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Production.Us.BaseUrl = settings.BaseUrl;
        }

        return options;
    }

    // -------------------------------------------------------------------------------------------------
    // Canned Maxio bodies (wire names, exactly as the SDK models expect them)
    // -------------------------------------------------------------------------------------------------

    public static readonly string ProductFamiliesJson = $$"""
        [ { "product_family": { "id": {{FamilyId}}, "handle": "{{FamilyHandle}}", "name": "eShop Subscribe" } } ]
        """;

    public static readonly string SiteJson = """
        { "site": { "id": 1, "subdomain": "cp-exp-2", "currency": "USD",
                    "relationship_invoicing_enabled": true,
                    "default_payment_collection_method": "automatic" } }
        """;

    public static readonly string ProductsJson = $$"""
        [
          { "product": { "id": 7126957, "handle": "{{ProPlanHandle}}", "name": "Pro Plan",
                         "description": "Everything, monthly", "price_in_cents": 29900,
                         "interval": 1, "interval_unit": "month", "require_credit_card": false,
                         "archived_at": null,
                         "product_family": { "id": {{FamilyId}}, "handle": "{{FamilyHandle}}" } } },
          { "product": { "id": 7126958, "handle": "basic-plan", "name": "Basic Plan",
                         "price_in_cents": 2900, "interval": 1, "interval_unit": "month",
                         "require_credit_card": false, "archived_at": null,
                         "product_family": { "id": {{FamilyId}}, "handle": "{{FamilyHandle}}" } } },
          { "product": { "id": 7126959, "handle": "retired-plan", "name": "Retired Plan",
                         "price_in_cents": 100, "interval": 1, "interval_unit": "month",
                         "archived_at": "2025-01-01T00:00:00-05:00",
                         "product_family": { "id": {{FamilyId}}, "handle": "{{FamilyHandle}}" } } }
        ]
        """;

    public static readonly string ProPlanJson = $$"""
        { "product": { "id": 7126957, "handle": "{{ProPlanHandle}}", "name": "Pro Plan",
                       "price_in_cents": 29900, "interval": 1, "interval_unit": "month",
                       "require_credit_card": false, "archived_at": null,
                       "product_family": { "id": {{FamilyId}}, "handle": "{{FamilyHandle}}" } } }
        """;

    public static readonly string ForeignPlanJson = $$"""
        { "product": { "id": 999, "handle": "{{ProPlanHandle}}", "name": "Someone else's plan",
                       "price_in_cents": 100, "interval": 1, "interval_unit": "month",
                       "product_family": { "id": 42, "handle": "another-family" } } }
        """;

    public static readonly string CustomerJson = $$"""
        { "customer": { "id": {{CustomerId}}, "reference": "eshoponweb-demouser@microsoft.com",
                        "email": "demouser@microsoft.com", "first_name": "Demouser", "last_name": "Customer" } }
        """;

    public static readonly string SubscriptionJson = $$"""
        { "subscription": { "id": {{SubscriptionId}}, "state": "active",
                            "current_period_ends_at": "2026-10-06T15:52:48-04:00",
                            "next_assessment_at": "2026-10-06T15:52:48-04:00",
                            "created_at": "2026-09-06T15:52:48-04:00",
                            "product_price_in_cents": 29900, "currency": "USD",
                            "payment_collection_method": "remittance",
                            "product": { "id": 7126957, "handle": "{{ProPlanHandle}}", "name": "Pro Plan" } } }
        """;

    public static readonly string SubscriptionListJson = $$"""
        [ {{SubscriptionJson}} ]
        """;

    public static readonly string CanceledSubscriptionListJson = $$"""
        [ { "subscription": { "id": 1, "state": "canceled",
                              "product": { "handle": "{{ProPlanHandle}}", "name": "Pro Plan" } } } ]
        """;
}
