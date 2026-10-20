using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Microsoft.eShopWeb.UnitTests.Billing;

/// <summary>
/// In-memory stand-in for the Maxio HTTP API, used as the HttpClient seam of the SDK client (no network).
/// Shared (linked) with the PublicApi integration tests; keep it free of any test-framework dependency.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    public sealed record FakePlan(string Handle, string Name, long PriceInCents, bool Archived = false);
    public sealed record FakeSubscription(int Id, int CustomerId, string Handle, string? Reference);
    public sealed record Seen(HttpMethod Method, string PathAndQuery, string? Body);

    private readonly object _lock = new();
    private int _nextId = 1000;

    public List<FakePlan> Plans { get; } = new() { new("fake-basic", "Fake Basic", 1500), new("fake-pro", "Fake Pro", 9900) };
    public Dictionary<string, int> Customers { get; } = new();                  // reference -> id
    public List<FakeSubscription> Subscriptions { get; } = new();
    public List<Seen> Requests { get; } = new();

    /// <summary>Return a response (or throw) to override the default behaviour of a request; null = default.</summary>
    public Func<HttpRequestMessage, string?, HttpResponseMessage?>? Intercept { get; set; }

    public int Count(HttpMethod method, string pathContains) =>
        Requests.Count(r => r.Method == method && r.PathAndQuery.Contains(pathContains, StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The SDK disposes request content after each attempt: read it now.
        var body = request.Content is null ? null : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        HttpResponseMessage response;
        lock (_lock)
        {
            Requests.Add(new Seen(request.Method, request.RequestUri!.PathAndQuery, body));
            response = Intercept?.Invoke(request, body) ?? Route(request, body);
        }
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    private HttpResponseMessage Route(HttpRequestMessage request, string? body)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);

        if (request.Method == HttpMethod.Get && path.EndsWith("/products.json", StringComparison.Ordinal))
        {
            var page = int.Parse(query["page"] ?? "1");
            var perPage = int.Parse(query["per_page"] ?? "20");
            var items = Plans.Skip((page - 1) * perPage).Take(perPage).Select(p =>
                new { product = new { handle = p.Handle, name = p.Name, price_in_cents = p.PriceInCents, interval = 1, interval_unit = "month",
                    archived_at = p.Archived ? (DateTime?)DateTime.UtcNow : null } });
            return Json(HttpStatusCode.OK, items);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json", StringComparison.Ordinal))
        {
            var reference = query["reference"]!;
            return Customers.TryGetValue(reference, out var id)
                ? Json(HttpStatusCode.OK, new { customer = new { id, reference } })
                : Json(HttpStatusCode.NotFound, new { errors = new[] { "Not Found" } });
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.Ordinal))
        {
            using var doc = JsonDocument.Parse(body!);
            var reference = doc.RootElement.GetProperty("customer").GetProperty("reference").GetString()!;
            var id = ++_nextId;
            Customers[reference] = id;
            return Json(HttpStatusCode.Created, new { customer = new { id, reference } });
        }

        if (request.Method == HttpMethod.Get && path.Contains("/customers/", StringComparison.Ordinal) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
        {
            var parts = path.Split((char)47);
            var customerId = int.Parse(parts[^2]);
            return Json(HttpStatusCode.OK, Subscriptions.Where(s => s.CustomerId == customerId).Select(SubscriptionJson));
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
        {
            using var doc = JsonDocument.Parse(body!);
            var sub = doc.RootElement.GetProperty("subscription");
            var handle = sub.GetProperty("product_handle").GetString()!;
            if (Plans.All(p => p.Handle != handle))
                return Json((HttpStatusCode)422, new { errors = new[] { "Product not found" } });
            var created = new FakeSubscription(++_nextId, sub.GetProperty("customer_id").GetInt32(), handle,
                sub.TryGetProperty("reference", out var r) ? r.GetString() : null);
            Subscriptions.Add(created);
            return Json(HttpStatusCode.Created, SubscriptionJson(created));
        }

        return Json(HttpStatusCode.NotFound, new { errors = new[] { "unrouted" } });
    }

    private object SubscriptionJson(FakeSubscription s)
    {
        var plan = Plans.First(p => p.Handle == s.Handle);
        return new
        {
            subscription = new
            {
                id = s.Id,
                state = "active",
                reference = s.Reference,
                product_price_in_cents = plan.PriceInCents,
                next_assessment_at = "2030-01-01T00:00:00Z",
                product = new { handle = plan.Handle, name = plan.Name, price_in_cents = plan.PriceInCents, interval = 1, interval_unit = "month",
                    product_family = new { handle = "test-family" } },
                customer = new { id = s.CustomerId }
            }
        };
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object payload) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
}
