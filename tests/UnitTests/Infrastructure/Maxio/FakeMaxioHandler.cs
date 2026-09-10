using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// A stateful in-memory stand-in for the Maxio REST API, covering exactly the operations the
/// integration calls. It lets the real <c>MaxioApiClient</c>/<c>MaxioSubscriptionService</c> run
/// end-to-end offline, and counts create calls so idempotency can be asserted.
/// </summary>
internal sealed class FakeMaxioHandler : HttpMessageHandler
{
    private readonly Dictionary<string, int> _customersByReference = new();
    private readonly List<(int Id, int CustomerId, string Handle, string State)> _subscriptions = new();
    private int _nextCustomerId = 100;
    private int _nextSubscriptionId = 5000;

    public int CreateCustomerCount { get; private set; }
    public int CreateSubscriptionCount { get; private set; }

    /// <summary>The plans returned by the fake product family; default is the seeded Basic/Pro pair.</summary>
    public List<(string Handle, string Name, long PriceInCents)> Plans { get; } = new()
    {
        ("basic-plan", "Basic Plan", 2900),
        ("eshop-pro", "Pro Plan", 29900),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var method = request.Method;

        if (method == HttpMethod.Get && path.StartsWith("/product_families/") && path.EndsWith("/products.json"))
        {
            var products = Plans.Select((p, i) =>
                $"{{\"product\":{{\"id\":{i + 1},\"name\":{JsonEncode(p.Name)},\"handle\":{JsonEncode(p.Handle)}," +
                $"\"price_in_cents\":{p.PriceInCents},\"interval\":1,\"interval_unit\":\"month\"," +
                "\"require_credit_card\":false,\"product_family\":{\"handle\":\"eshop-subscribe\"}}}");
            return Json("[" + string.Join(",", products) + "]");
        }

        if (method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            var reference = ParseReference(request.RequestUri.Query);
            if (_customersByReference.TryGetValue(reference, out var id))
            {
                return Json(CustomerJson(id, reference));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("customer not found") };
        }

        if (method == HttpMethod.Post && path == "/customers.json")
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var reference = doc.RootElement.GetProperty("customer").GetProperty("reference").GetString() ?? string.Empty;

            CreateCustomerCount++;
            if (!_customersByReference.TryGetValue(reference, out var id))
            {
                id = _nextCustomerId++;
                _customersByReference[reference] = id;
            }

            return Json(CustomerJson(id, reference));
        }

        if (method == HttpMethod.Get && path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            var customerId = int.Parse(path.Split('/')[2]);
            var reference = _customersByReference.FirstOrDefault(kv => kv.Value == customerId).Key;
            var subs = _subscriptions
                .Where(s => s.CustomerId == customerId)
                .Select(s => SubscriptionJson(s.Id, customerId, reference, s.Handle, s.State));
            return Json("[" + string.Join(",", subs) + "]");
        }

        if (method == HttpMethod.Post && path == "/subscriptions.json")
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var subscription = doc.RootElement.GetProperty("subscription");
            var handle = subscription.GetProperty("product_handle").GetString()!;
            var customerId = subscription.GetProperty("customer_id").GetInt32();
            var reference = _customersByReference.FirstOrDefault(kv => kv.Value == customerId).Key;

            CreateSubscriptionCount++;
            var id = _nextSubscriptionId++;
            _subscriptions.Add((id, customerId, handle, "active"));

            return Json(SubscriptionJson(id, customerId, reference, handle, "active"), HttpStatusCode.Created);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"unhandled {method} {path}")
        };
    }

    private string CustomerJson(int id, string reference) =>
        $"{{\"customer\":{{\"id\":{id},\"first_name\":\"Demo\",\"last_name\":\"User\"," +
        $"\"email\":{JsonEncode(reference)},\"reference\":{JsonEncode(reference)}}}}}";

    private string SubscriptionJson(int id, int customerId, string? reference, string handle, string state)
    {
        var plan = Plans.First(p => p.Handle == handle);
        return $"{{\"subscription\":{{\"id\":{id},\"state\":{JsonEncode(state)}," +
               $"\"product_price_in_cents\":{plan.PriceInCents}," +
               "\"current_period_ends_at\":\"2026-10-10T00:00:00Z\",\"next_assessment_at\":\"2026-10-10T00:00:00Z\"," +
               "\"created_at\":\"2026-09-10T00:00:00Z\"," +
               $"\"product\":{{\"handle\":{JsonEncode(handle)},\"name\":{JsonEncode(plan.Name)},\"price_in_cents\":{plan.PriceInCents}}}," +
               $"\"customer\":{{\"id\":{customerId},\"reference\":{JsonEncode(reference ?? string.Empty)}}}}}}}";
    }

    private static string ParseReference(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0] == "reference")
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return string.Empty;
    }

    private static string JsonEncode(string value) => JsonSerializer.Serialize(value);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}
