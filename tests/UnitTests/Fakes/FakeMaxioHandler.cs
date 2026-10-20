using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.TestFakes;

/// <summary>
/// In-memory stand-in for the Maxio HTTP API (the SDK's HttpClient seam). Shared by unit and
/// PublicApi integration tests (linked file).
/// </summary>
public sealed class FakeMaxioHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private int _nextCustomerId = 100;
    private int _nextSubscriptionId = 1000;

    public record FakeProduct(string Handle, string Name, long PriceInCents, bool Archived = false);
    private record FakeCustomer(int Id, string Reference);
    private record FakeSubscription(int Id, int CustomerId, string Handle);

    public List<FakeProduct> Products { get; } = new()
    {
        new("eshop-pro", "eShop Pro", 29900),
        new("basic-plan", "Basic", 2900),
        new("old-plan", "Old", 100, Archived: true),
    };

    public List<string> Calls { get; } = new();
    public List<string> Bodies { get; } = new();

    /// <summary>The write lands at "Maxio" but the connection drops before the answer arrives.</summary>
    public bool LandThenDropCreateCustomer { get; set; }
    public bool LandThenDropCreateSubscription { get; set; }
    /// <summary>The connection drops before the write lands.</summary>
    public bool DropBeforeCreateSubscription { get; set; }
    public bool Reject422CreateSubscription { get; set; }
    public bool FailListSubscriptions { get; set; }
    public TimeSpan CreateSubscriptionDelay { get; set; }

    private readonly List<FakeCustomer> _customers = new();
    private readonly List<FakeSubscription> _subscriptions = new();

    public int Count(string method, string pathContains)
    {
        lock (_gate)
        {
            return Calls.Count(c => c.StartsWith(method + " ", StringComparison.Ordinal) && c.Contains(pathContains, StringComparison.Ordinal));
        }
    }

    public int SubscriptionCount { get { lock (_gate) return _subscriptions.Count; } }
    public int CustomerCount { get { lock (_gate) return _customers.Count; } }

    /// <summary>Marks every subscription at "Maxio" with the given state-name (test hook for state mapping).</summary>
    public string SubscriptionState { get; set; } = "active";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            Calls.Add($"{request.Method} {path}{request.RequestUri.Query}");
            if (body is not null) Bodies.Add(body);
        }

        var method = request.Method.Method;

        if (method == "GET" && path.Contains("/product_families/") && path.EndsWith("/products.json"))
        {
            // Pages beyond the first are empty.
            var query = request.RequestUri.Query;
            if (query.Contains("page=") && !query.Contains("page=1&") && !query.EndsWith("page=1"))
            {
                return Json(HttpStatusCode.OK, "[]");
            }
            lock (_gate)
            {
                return Json(HttpStatusCode.OK, "[" + string.Join(",", Products.Select(p =>
                    "{\"product\":{" + ProductJson(p) + (p.Archived ? ",\"archived_at\":\"2020-01-01T00:00:00Z\"" : "") + "}}")) + "]");
            }
        }

        if (method == "GET" && path.EndsWith("/customers/lookup.json"))
        {
            var reference = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["reference"];
            lock (_gate)
            {
                var c = _customers.FirstOrDefault(x => x.Reference == reference);
                return c is null
                    ? Json(HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}")
                    : Json(HttpStatusCode.OK, CustomerJson(c));
            }
        }

        if (method == "POST" && path.EndsWith("/customers.json"))
        {
            using var doc = JsonDocument.Parse(body!);
            var reference = doc.RootElement.GetProperty("customer").GetProperty("reference").GetString()!;
            FakeCustomer created;
            lock (_gate)
            {
                created = new FakeCustomer(_nextCustomerId++, reference);
                _customers.Add(created);
            }
            if (LandThenDropCreateCustomer) throw new HttpRequestException("connection reset");
            return Json(HttpStatusCode.Created, CustomerJson(created));
        }

        if (method == "GET" && path.Contains("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            if (FailListSubscriptions) throw new HttpRequestException("connection reset");
            var customerId = int.Parse(path.Split('/')[^2]);
            lock (_gate)
            {
                return Json(HttpStatusCode.OK, "[" + string.Join(",",
                    _subscriptions.Where(s => s.CustomerId == customerId).Select(SubscriptionJson)) + "]");
            }
        }

        if (method == "POST" && path.EndsWith("/subscriptions.json"))
        {
            if (CreateSubscriptionDelay > TimeSpan.Zero) await Task.Delay(CreateSubscriptionDelay, cancellationToken);
            if (DropBeforeCreateSubscription) throw new HttpRequestException("connection reset");
            if (Reject422CreateSubscription) return Json((HttpStatusCode)422, "{\"errors\":[\"Product is invalid\"]}");

            using var doc = JsonDocument.Parse(body!);
            var sub = doc.RootElement.GetProperty("subscription");
            var handle = sub.GetProperty("product_handle").GetString()!;
            var customerId = sub.GetProperty("customer_id").GetInt32();
            FakeSubscription created;
            lock (_gate)
            {
                created = new FakeSubscription(_nextSubscriptionId++, customerId, handle);
                _subscriptions.Add(created);
            }
            if (LandThenDropCreateSubscription) throw new HttpRequestException("connection reset");
            return Json(HttpStatusCode.Created, SubscriptionJson(created, wrap: true));
        }

        if (method == "GET" && path.Contains("/subscriptions/") && path.EndsWith(".json"))
        {
            var id = int.Parse(path.Split('/')[^1].Replace(".json", ""));
            lock (_gate)
            {
                var s = _subscriptions.FirstOrDefault(x => x.Id == id);
                return s is null
                    ? Json(HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}")
                    : Json(HttpStatusCode.OK, SubscriptionJson(s, wrap: true));
            }
        }

        return Json(HttpStatusCode.NotFound, "{\"errors\":[\"unrouted\"]}");
    }

    private static string ProductJson(FakeProduct p) =>
        $"\"id\":{Math.Abs(p.Handle.GetHashCode()) % 1000},\"name\":\"{p.Name}\",\"handle\":\"{p.Handle}\",\"price_in_cents\":{p.PriceInCents},\"interval\":1,\"interval_unit\":\"month\"";

    private static string CustomerJson(FakeCustomer c) =>
        $"{{\"customer\":{{\"id\":{c.Id},\"reference\":\"{c.Reference}\",\"email\":\"x@example.test\"}}}}";

    private string SubscriptionJson(FakeSubscription s) => SubscriptionJson(s, wrap: false);

    private string SubscriptionJson(FakeSubscription s, bool wrap)
    {
        var product = Products.First(p => p.Handle == s.Handle);
        var inner = $"{{\"id\":{s.Id},\"state\":\"{SubscriptionState}\",\"next_assessment_at\":\"2030-01-15T00:00:00Z\"," +
                    $"\"current_period_ends_at\":\"2030-01-15T00:00:00Z\",\"product\":{{{ProductJson(product)}}},\"customer\":{{\"id\":{s.CustomerId}}}}}";
        return wrap ? "{\"subscription\":" + inner + "}" : "{\"subscription\":" + inner + "}";
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
