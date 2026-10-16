using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;

namespace PublicApiIntegrationTests.Subscriptions;

// Exercises the real generated SDK, including its serialization, error types, and transport-failure behavior.
internal sealed class MaxioStub : HttpMessageHandler
{
    public ConcurrentDictionary<string, JsonElement> Customers { get; } = new();
    public ConcurrentDictionary<string, JsonElement> Subscriptions { get; } = new();
    public ConcurrentQueue<(string Method, string Url, string? Body)> Requests { get; } = new();
    public bool DropCustomerResponse;
    public bool DropCustomerBeforeCommit;
    public bool DropSubscriptionResponse;
    public bool DropSubscriptionBeforeCommit;
    public bool MalformedSubscriptionSuccess;
    public bool MalformedCatalog;
    public bool RejectSubscription;
    public bool FullCatalogPages;
    public int? CatalogError;
    public int CustomerWrites;
    public int SubscriptionWrites;
    private int _customerId = 100;
    private int _subscriptionId = 200;

    private static object Product(string handle) => new
    {
        id = handle == "plan-a" ? 51 : 52, handle, name = handle, price_in_cents = 1700,
        interval = 1, interval_unit = "month", require_credit_card = false,
        product_family = new { id = 999, handle = "test-family" }
    };

    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);
    private static HttpResponseMessage Reply(HttpRequestMessage request, int status, object value) => new((HttpStatusCode)status)
    {
        RequestMessage = request,
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Enqueue((request.Method.Method, request.RequestUri.ToString(), body));
        if (path.EndsWith("/products.json"))
        {
            if (CatalogError != null) return Reply(request, CatalogError.Value, new { errors = new[] { "provider detail must not escape" } });
            if (MalformedCatalog) return Reply(request, 200, new[] { new { product = "invalid" } });
            if (FullCatalogPages) return Reply(request, 200, Enumerable.Repeat(new { product = Product("plan-a") }, 200));
            return Reply(request, 200, new[] { new { product = Product("plan-a") }, new { product = Product("plan-b") } });
        }
        if (path.EndsWith("/customers/lookup.json"))
        {
            var reference = QueryHelpers.ParseQuery(request.RequestUri.Query)["reference"].ToString();
            return Customers.TryGetValue(reference, out var customer)
                ? Reply(request, 200, new { customer }) : Reply(request, 404, new { });
        }
        if (path.EndsWith("/subscriptions/lookup.json"))
        {
            var reference = QueryHelpers.ParseQuery(request.RequestUri.Query)["reference"].ToString();
            return Subscriptions.TryGetValue(reference, out var subscription)
                ? Reply(request, 200, new { subscription }) : Reply(request, 404, new { });
        }
        if (path.EndsWith("/customers.json") && request.Method == HttpMethod.Post)
        {
            Interlocked.Increment(ref CustomerWrites);
            if (DropCustomerBeforeCommit) throw new HttpRequestException("Simulated unknown customer write");
            using var doc = JsonDocument.Parse(body!);
            var c = doc.RootElement.GetProperty("customer");
            var reference = c.GetProperty("reference").GetString()!;
            var customer = Element(new { id = Interlocked.Increment(ref _customerId), reference });
            if (!Customers.TryAdd(reference, customer)) return Reply(request, 422, new { errors = new[] { "Reference taken" } });
            await Task.Delay(20, ct); // Keep the owner in flight while competing HTTP requests acquire claims.
            if (DropCustomerResponse) throw new HttpRequestException("Simulated lost customer response after commit");
            return Reply(request, 201, new { customer });
        }
        if (path.EndsWith("/subscriptions.json") && request.Method == HttpMethod.Post)
        {
            Interlocked.Increment(ref SubscriptionWrites);
            if (RejectSubscription) return Reply(request, 422, new { errors = new[] { "Invalid request detail" } });
            if (DropSubscriptionBeforeCommit) throw new HttpRequestException("Simulated send with unknown outcome");
            using var doc = JsonDocument.Parse(body!);
            var s = doc.RootElement.GetProperty("subscription");
            var reference = s.GetProperty("reference").GetString()!;
            var handle = s.GetProperty("product_handle").GetString()!;
            var customer = Customers.Values.Single(c => c.GetProperty("id").GetInt32() == s.GetProperty("customer_id").GetInt32());
            var subscription = Element(new
            {
                id = Interlocked.Increment(ref _subscriptionId), reference, customer, product = Product(handle),
                state = "active", product_price_in_cents = 1700, currency = "USD",
                next_assessment_at = "2027-01-01T00:00:00Z", current_period_ends_at = "2027-01-01T00:00:00Z"
            });
            Subscriptions[reference] = subscription;
            await Task.Delay(20, ct);
            if (DropSubscriptionResponse) throw new HttpRequestException("Simulated lost subscription response after commit");
            if (MalformedSubscriptionSuccess) return Reply(request, 201, new { subscription = "invalid" });
            return Reply(request, 201, new { subscription });
        }
        if (path.Contains("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            var id = int.Parse(path.Split('/')[^2]);
            return Reply(request, 200, Subscriptions.Values.Where(s => s.GetProperty("customer").GetProperty("id").GetInt32() == id)
                .Select(subscription => new { subscription }));
        }
        throw new InvalidOperationException("Unexpected SDK request: " + path);
    }
}
