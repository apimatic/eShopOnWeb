using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the real <see cref="MaxioApiClient"/> and <see cref="MaxioSubscriptionBillingService"/>
/// over a scripted transport, so the tests exercise the actual HTTP binding - paths, envelopes,
/// error parsing - and not a hand-written stand-in for it.
/// </summary>
internal sealed class MaxioTestHost
{
    public MaxioTestHost(MaxioSettings? settings = null)
    {
        Settings = settings ?? new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = "eshop-subscribe",
            // Caching off by default so each test sees exactly the calls it makes.
            CatalogCacheDuration = TimeSpan.Zero,
            MaxRetryAttempts = 0,
        };

        Transport = new ScriptedTransport();

        var monitor = new StaticOptionsMonitor(Settings);

        // Same pipeline the DI registration builds, so the retry policy is under test too.
        var pipeline = new MaxioRetryHandler(monitor, NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = Transport,
        };

        var httpClient = new HttpClient(pipeline) { BaseAddress = Settings.ResolveBaseAddress() };
        Client = new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);

        Service = new MaxioSubscriptionBillingService(
            Client,
            new MemoryCache(new MemoryCacheOptions()),
            monitor,
            new KeyedAsyncLock(),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    public MaxioSettings Settings { get; }

    public ScriptedTransport Transport { get; }

    public MaxioApiClient Client { get; }

    public ISubscriptionBillingService Service { get; }

    /// <summary>Registers the reads a healthy site with one product family and two plans answers.</summary>
    public MaxioTestHost WithStandardCatalog()
    {
        Transport.OnGet("/site.json", Json("""
            {"site":{"id":1,"name":"Test","subdomain":"test-site","currency":"USD","relationship_invoicing_enabled":true}}
            """));

        Transport.OnGet("/product_families.json", Json("""
            [{"product_family":{"id":42,"name":"eShopSubscribe","handle":"eshop-subscribe","archived_at":null}}]
            """));

        Transport.OnGet("/product_families/42/products.json", Json("""
            [{"product":{"id":700,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,
              "interval_unit":"month","require_credit_card":false,"archived_at":null,
              "product_family":{"id":42,"handle":"eshop-subscribe","name":"eShopSubscribe"}}},
             {"product":{"id":701,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,
              "interval_unit":"month","require_credit_card":false,"archived_at":null,
              "product_family":{"id":42,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}]
            """));

        return this;
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound)
    {
        Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
    };

    /// <summary>The 422 Advanced Billing returns when a caller-assigned reference is already taken.</summary>
    public static HttpResponseMessage ReferenceTaken() =>
        Json("""{"errors":["Reference: must be unique - that value has been taken."]}""", HttpStatusCode.UnprocessableEntity);

    private sealed class StaticOptionsMonitor : IOptionsMonitor<MaxioSettings>
    {
        public StaticOptionsMonitor(MaxioSettings value) => CurrentValue = value;

        public MaxioSettings CurrentValue { get; }

        public MaxioSettings Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<MaxioSettings, string?> listener) => null;
    }
}
