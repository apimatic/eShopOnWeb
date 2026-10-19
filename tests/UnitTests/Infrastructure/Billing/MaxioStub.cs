using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>Fake HttpMessageHandler: the SDK's test seam. Records every request and buffers its body.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

    public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) => _responder = responder;

    public List<(HttpMethod Method, string Path, string Query, string? Body)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the body now: the SDK disposes request content after each attempt.
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests)
        {
            Requests.Add((request.Method, Uri.UnescapeDataString(request.RequestUri!.AbsolutePath), Uri.UnescapeDataString(request.RequestUri.Query), body));
        }

        var response = _responder(request, body);
        response.RequestMessage = request;
        return response;
    }

    public int Count(HttpMethod method, string path)
    {
        lock (Requests)
        {
            return Requests.Count(r => r.Method == method && r.Path == path);
        }
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Empty(HttpStatusCode status) =>
        new(status) { Content = new StringContent(string.Empty) };
}

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

public static class MaxioTestHost
{
    public const string ProductsPath = "/product_families/handle:example-family/products.json";
    public const string CustomerLookupPath = "/customers/lookup.json";
    public const string CustomersPath = "/customers.json";
    public const string SubscriptionsPath = "/subscriptions.json";
    public const string SubscriptionLookupPath = "/subscriptions/lookup.json";

    public static MaxioSettings Settings() => new()
    {
        ApiKey = "test-placeholder-key",
        Subdomain = "example-site",
        ProductFamilyHandle = "example-family"
    };

    public static DbContextOptions<CatalogContext> NewDatabase() =>
        new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("billing-" + Guid.NewGuid())
            .Options;

    public static MaxioAdvancedBillingClient Client(StubHandler handler, TimeProvider clock)
    {
        var options = MaxioBillingServiceCollectionExtensions.CreateClientOptions(Settings(), NullLoggerFactory.Instance, clock);
        // Keep the production per-attempt timeout but no backoff waits in unit tests.
        options.Retry = RetryOptions.Disabled() with { Timeout = MaxioBillingServiceCollectionExtensions.PerAttemptTimeout };
        return new MaxioAdvancedBillingClient(new HttpClient(handler), options);
    }

    public static MaxioSubscriptionBillingService Service(StubHandler handler, DbContextOptions<CatalogContext> db, TimeProvider? clock = null)
    {
        clock ??= new ManualTimeProvider();
        return new MaxioSubscriptionBillingService(
            Client(handler, clock),
            new CatalogContext(db),
            Options.Create(Settings()),
            NullLogger<MaxioSubscriptionBillingService>.Instance,
            clock);
    }

    public static string Product(string handle, long priceInCents, bool archived = false) =>
        "{\"product\":{\"id\":" + Math.Abs(handle.GetHashCode() % 100000) +
        ",\"name\":\"" + handle + " name\",\"handle\":\"" + handle + "\",\"price_in_cents\":" + priceInCents +
        ",\"interval\":1,\"interval_unit\":\"month\"" +
        (archived ? ",\"archived_at\":\"2025-01-01T00:00:00Z\"" : "") + "}}";

    public static string Products(params string[] items) => "[" + string.Join(",", items) + "]";

    public static string Customer(int id, string reference) =>
        "{\"customer\":{\"id\":" + id + ",\"reference\":\"" + reference + "\",\"email\":\"" + reference + "\"}}";

    public static string Subscription(int id, string handle, string? reference, string state = "active") =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"" + state + "\",\"product_price_in_cents\":29900" +
        ",\"next_assessment_at\":\"2026-11-19T12:00:00Z\",\"created_at\":\"2026-10-19T12:00:00Z\"" +
        ",\"reference\":" + (reference is null ? "null" : "\"" + reference + "\"") +
        ",\"product\":{\"id\":7,\"name\":\"" + handle + " name\",\"handle\":\"" + handle + "\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}" +
        ",\"customer\":{\"id\":42}}}";
}
