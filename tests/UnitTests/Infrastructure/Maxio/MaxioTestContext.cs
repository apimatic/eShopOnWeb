using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Wires a real <see cref="MaxioApiClient"/> and <see cref="MaxioSubscriptionBillingService"/> over a
/// stubbed transport, so the tests cover the actual request/response handling rather than a mock of it.
/// </summary>
public class MaxioTestContext
{
    public MaxioTestContext(Func<HttpRequestMessage, int, StubResponse> responder, Action<MaxioSettings>? configure = null)
    {
        Settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family",
            // Keep the tests fast and deterministic.
            MaxRetryAttempts = 2,
            RetryBaseDelayMilliseconds = 1,
            PlanCacheSeconds = 0,
            SiteCacheSeconds = 0
        };
        configure?.Invoke(Settings);

        Handler = new StubHttpMessageHandler(responder);

        var options = Options.Create(Settings);
        var httpClient = new HttpClient(Handler) { BaseAddress = Settings.ResolveBaseAddress() };

        Client = new MaxioApiClient(httpClient, options, NullLogger<MaxioApiClient>.Instance);
        Cache = new MemoryCache(new MemoryCacheOptions());
        Service = new MaxioSubscriptionBillingService(Client, options, Cache, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    public MaxioSettings Settings { get; }

    public StubHttpMessageHandler Handler { get; }

    public MaxioApiClient Client { get; }

    public IMemoryCache Cache { get; }

    public MaxioSubscriptionBillingService Service { get; }
}

/// <summary>Canned Maxio payloads, trimmed to the fields the integration reads.</summary>
public static class MaxioPayloads
{
    public const string Site = """
        {"site":{"id":1,"name":"Test Site","subdomain":"test-site","currency":"USD","relationship_invoicing_enabled":true,"test":true}}
        """;

    public const string StatementBasedSite = """
        {"site":{"id":1,"name":"Legacy Site","subdomain":"test-site","currency":"USD","relationship_invoicing_enabled":false,"test":true}}
        """;

    public const string Products = """
        [
          {"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","description":null,"price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,"product_family":{"id":9,"name":"eShopSubscribe","handle":"test-family"}}},
          {"product":{"id":2,"name":"Basic Plan","handle":"basic-plan","description":null,"price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null,"product_family":{"id":9,"name":"eShopSubscribe","handle":"test-family"}}},
          {"product":{"id":3,"name":"Retired Plan","handle":"retired","description":null,"price_in_cents":100,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":"2020-01-01T00:00:00+00:00","product_family":{"id":9,"name":"eShopSubscribe","handle":"test-family"}}}
        ]
        """;

    public const string Customer = """
        {"customer":{"id":555,"first_name":"Demouser","last_name":"Shopper","email":"demouser@microsoft.com","reference":"eshoponweb:demouser@microsoft.com","created_at":"2026-09-06T10:00:00+00:00"}}
        """;

    public const string NoSubscriptions = "[]";

    public const string ProSubscription = """
        {"subscription":{"id":777,"state":"active","product_price_in_cents":29900,
          "current_period_started_at":"2026-09-06T10:00:00+00:00","current_period_ends_at":"2026-10-06T10:00:00+00:00",
          "next_assessment_at":"2026-10-06T10:00:00+00:00","activated_at":"2026-09-06T10:00:01+00:00",
          "created_at":"2026-09-06T10:00:00+00:00",
          "customer":{"id":555,"reference":"eshoponweb:demouser@microsoft.com","email":"demouser@microsoft.com"},
          "product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}
        """;

    public const string ProSubscriptionList = $"[{ProSubscription}]";

    public const string CanceledProSubscriptionList = """
        [{"subscription":{"id":776,"state":"canceled","product_price_in_cents":29900,
          "canceled_at":"2026-08-01T10:00:00+00:00","created_at":"2026-07-06T10:00:00+00:00",
          "customer":{"id":555,"reference":"eshoponweb:demouser@microsoft.com"},
          "product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}]
        """;
}
