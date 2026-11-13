using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// An in-process stand-in for the Maxio API, installed as the primary handler of the SDK's named HttpClient.
/// It answers the six routes the integration uses, keeps customers/subscriptions in memory, records every
/// request (with its body, captured while still readable) and can be told to fail in specific ways.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private int _nextCustomerId = 1000;
    private int _nextSubscriptionId = 5000;
    private readonly ConcurrentDictionary<string, JsonObject> _customersByReference = new();
    private readonly ConcurrentDictionary<int, List<JsonObject>> _subscriptionsByCustomer = new();

    public string FamilyHandle { get; set; } = "test-family";

    public List<JsonObject> Products { get; } = new()
    {
        Product(1, "test-pro", "Pro Plan", 29900),
        Product(2, "test-basic", "Basic Plan", 2900),
        Product(3, "test-retired", "Retired Plan", 100, archived: true)
    };

    public ConcurrentQueue<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = new();

    /// <summary>Delay applied to POST /subscriptions.json, to widen race windows.</summary>
    public TimeSpan CreateSubscriptionDelay { get; set; }

    /// <summary>Next POST /subscriptions.json drops the connection after the subscription was created.</summary>
    public bool DropConnectionAfterCreatingSubscription { get; set; }

    /// <summary>Next POST /customers.json drops the connection after the customer was created.</summary>
    public bool DropConnectionAfterCreatingCustomer { get; set; }

    /// <summary>Next POST /subscriptions.json drops the connection before anything was created.</summary>
    public bool DropConnectionBeforeCreatingSubscription { get; set; }

    /// <summary>Next POST /subscriptions.json is refused with 422 and this message.</summary>
    public string? RejectNextSubscriptionWith { get; set; }

    /// <summary>Number of upcoming GET /subscriptions/lookup.json calls that fail at the transport.</summary>
    public int FailSubscriptionLookups;

    public int Count(HttpMethod method, string path) =>
        Requests.Count(r => r.Method == method && r.PathAndQuery.Split('?')[0] == path);

    public IEnumerable<string> Bodies(HttpMethod method, string path) =>
        Requests.Where(r => r.Method == method && r.PathAndQuery.Split('?')[0] == path).Select(r => r.Body ?? "");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;
        Requests.Enqueue((request.Method, uri.PathAndQuery, body));

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        if (request.Method == HttpMethod.Get && path == $"/product_families/handle%3A{FamilyHandle}/products.json"
            || request.Method == HttpMethod.Get && uri.OriginalString.Contains($"/product_families/handle%3A{FamilyHandle}/products.json"))
        {
            var page = int.Parse(query["page"] ?? "1");
            var perPage = int.Parse(query["per_page"] ?? "20");
            var items = Products.Skip((page - 1) * perPage).Take(perPage)
                .Select(p => new JsonObject { ["product"] = p.DeepClone() });
            return Json(HttpStatusCode.OK, new JsonArray(items.ToArray<JsonNode?>()));
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            return _customersByReference.TryGetValue(query["reference"] ?? "", out var c)
                ? Json(HttpStatusCode.OK, new JsonObject { ["customer"] = c.DeepClone() })
                : Json(HttpStatusCode.NotFound, new JsonObject { ["errors"] = new JsonArray("Not Found") });
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            var input = JsonNode.Parse(body!)!["customer"]!.AsObject();
            var reference = (string?)input["reference"] ?? "";
            if (_customersByReference.ContainsKey(reference))
            {
                return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Reference: must be unique") });
            }
            var customer = new JsonObject
            {
                ["id"] = Interlocked.Increment(ref _nextCustomerId),
                ["first_name"] = (string?)input["first_name"],
                ["last_name"] = (string?)input["last_name"],
                ["email"] = (string?)input["email"],
                ["reference"] = reference
            };
            _customersByReference[reference] = customer;
            if (DropConnectionAfterCreatingCustomer)
            {
                DropConnectionAfterCreatingCustomer = false;
                throw new HttpRequestException("connection reset (after customer created)");
            }
            return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = customer.DeepClone() });
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            var id = int.Parse(path.Split('/')[2]);
            var list = _subscriptionsByCustomer.GetOrAdd(id, _ => new List<JsonObject>());
            lock (list)
            {
                return Json(HttpStatusCode.OK, new JsonArray(list.Select(s => (JsonNode?)new JsonObject { ["subscription"] = s.DeepClone() }).ToArray()));
            }
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            if (CreateSubscriptionDelay > TimeSpan.Zero)
            {
                await Task.Delay(CreateSubscriptionDelay, ct);
            }
            if (DropConnectionBeforeCreatingSubscription)
            {
                DropConnectionBeforeCreatingSubscription = false;
                throw new HttpRequestException("connection reset (before provider acted)");
            }
            if (RejectNextSubscriptionWith is { } message)
            {
                RejectNextSubscriptionWith = null;
                return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray(message) });
            }

            var input = JsonNode.Parse(body!)!["subscription"]!.AsObject();
            var customerId = (int)input["customer_id"]!;
            var product = Products.First(p => (string?)p["handle"] == (string?)input["product_handle"]);
            var now = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
            var subscription = new JsonObject
            {
                ["id"] = Interlocked.Increment(ref _nextSubscriptionId),
                ["state"] = "active",
                ["reference"] = (string?)input["reference"],
                ["product_price_in_cents"] = (long)product["price_in_cents"]!,
                ["currency"] = "USD",
                ["created_at"] = now.ToString("O"),
                ["current_period_ends_at"] = now.AddMonths(1).ToString("O"),
                ["next_assessment_at"] = now.AddMonths(1).ToString("O"),
                ["product"] = product.DeepClone(),
                ["customer"] = new JsonObject { ["id"] = customerId }
            };
            var list = _subscriptionsByCustomer.GetOrAdd(customerId, _ => new List<JsonObject>());
            lock (list)
            {
                list.Add(subscription);
            }

            if (DropConnectionAfterCreatingSubscription)
            {
                DropConnectionAfterCreatingSubscription = false;
                throw new HttpRequestException("connection reset (after provider acted)");
            }
            return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = subscription.DeepClone() });
        }

        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            if (Interlocked.Decrement(ref FailSubscriptionLookups) >= 0)
            {
                throw new HttpRequestException("connection reset (lookup)");
            }
            var reference = query["reference"];
            var match = _subscriptionsByCustomer.Values.SelectMany(l => { lock (l) { return l.ToList(); } })
                .FirstOrDefault(s => (string?)s["reference"] == reference);
            return match is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") }
                : Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = match.DeepClone() });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"FakeMaxio: no route for {request.Method} {uri.PathAndQuery}") };
    }

    private static JsonObject Product(int id, string handle, string name, long priceInCents, bool archived = false) => new()
    {
        ["id"] = id,
        ["handle"] = handle,
        ["name"] = name,
        ["price_in_cents"] = priceInCents,
        ["interval"] = 1,
        ["interval_unit"] = "month",
        ["archived_at"] = archived ? "2026-01-01T00:00:00Z" : null
    };

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) => new(status)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json")
    };
}
