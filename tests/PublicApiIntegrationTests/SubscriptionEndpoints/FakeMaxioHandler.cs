using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>A small stateful stand-in for the Maxio endpoints the subscription flow uses.</summary>
public sealed class FakeMaxioHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _customersByReference = new();
    private readonly List<(int Id, int CustomerId, string Plan, string? Reference)> _subscriptions = new();
    private int _nextId = 1000;

    public FakeMaxioHandler(string familyHandle) => FamilyHandle = familyHandle;

    public string FamilyHandle { get; }
    public int CustomerCreates { get; private set; }
    public int SubscriptionCreates { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        // Widen the race window so concurrent requests really overlap.
        await Task.Delay(50, cancellationToken);
        lock (_gate)
        {
            return Route(request, body);
        }
    }

    private HttpResponseMessage Route(HttpRequestMessage request, string? body)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);

        if (request.Method == HttpMethod.Get && path == $"/product_families/handle:{FamilyHandle}/products.json")
        {
            return Json(HttpStatusCode.OK, "[" + Product(1, "eshop-pro", 29900) + "," + Product(2, "basic-plan", 2900) + "]");
        }
        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            return _customersByReference.TryGetValue(query["reference"]!, out var id)
                ? Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { customer = new { id, reference = query["reference"] } }))
                : Json(HttpStatusCode.NotFound, "{}");
        }
        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            var customer = JsonDocument.Parse(body!).RootElement.GetProperty("customer");
            var reference = customer.GetProperty("reference").GetString()!;
            if (_customersByReference.ContainsKey(reference))
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":{"customer":"Reference must be unique"}}""");
            }
            CustomerCreates++;
            var id = _nextId++;
            _customersByReference[reference] = id;
            return Json(HttpStatusCode.Created, JsonSerializer.Serialize(new { customer = new { id, reference } }));
        }
        var customerSubs = Regex.Match(path, @"^/customers/(\d+)/subscriptions\.json$");
        if (request.Method == HttpMethod.Get && customerSubs.Success)
        {
            var customerId = int.Parse(customerSubs.Groups[1].Value);
            return Json(HttpStatusCode.OK, "[" + string.Join(",", _subscriptions.Where(s => s.CustomerId == customerId).Select(Subscription)) + "]");
        }
        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            var subscription = JsonDocument.Parse(body!).RootElement.GetProperty("subscription");
            var created = (Id: _nextId++, CustomerId: subscription.GetProperty("customer_id").GetInt32(),
                Plan: subscription.GetProperty("product_handle").GetString()!, Reference: subscription.GetProperty("reference").GetString());
            SubscriptionCreates++;
            _subscriptions.Add(created);
            return Json(HttpStatusCode.Created, Subscription(created));
        }
        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            var found = _subscriptions.FirstOrDefault(s => s.Reference == query["reference"]);
            return found.Id != 0 ? Json(HttpStatusCode.OK, Subscription(found)) : Json(HttpStatusCode.NotFound, "{}");
        }
        var single = Regex.Match(path, @"^/subscriptions/(\d+)\.json$");
        if (request.Method == HttpMethod.Get && single.Success)
        {
            var found = _subscriptions.FirstOrDefault(s => s.Id == int.Parse(single.Groups[1].Value));
            return found.Id != 0 ? Json(HttpStatusCode.OK, Subscription(found)) : Json(HttpStatusCode.NotFound, "{}");
        }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private string Subscription((int Id, int CustomerId, string Plan, string? Reference) s) =>
        JsonSerializer.Serialize(new
        {
            subscription = new
            {
                id = s.Id,
                state = "active",
                reference = s.Reference,
                customer = new { id = s.CustomerId },
                product = new { handle = s.Plan, name = "Plan " + s.Plan, product_family = new { handle = FamilyHandle } },
                product_price_in_cents = s.Plan == "eshop-pro" ? 29900 : 2900,
                currency = "USD",
                next_assessment_at = "2026-11-15T00:00:00Z",
                current_period_ends_at = "2026-11-15T00:00:00Z",
                created_at = "2026-10-15T00:00:00Z",
            }
        });

    private static string Product(int id, string handle, long price) =>
        JsonSerializer.Serialize(new
        {
            product = new { id, handle, name = "Plan " + handle, price_in_cents = price, interval = 1, interval_unit = "month" }
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
