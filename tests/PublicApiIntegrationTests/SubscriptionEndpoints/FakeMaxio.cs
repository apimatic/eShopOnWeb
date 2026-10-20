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
using System.Web;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// An in-memory stand-in for the Maxio endpoints the integration uses, plugged in under the real SDK
/// client as its HttpMessageHandler. Records every request (with its body, buffered while readable).
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private int _nextId = 1000;

    public ConcurrentDictionary<string, int> Customers { get; } = new();
    public ConcurrentDictionary<string, JsonObject> Subscriptions { get; } = new();
    public ConcurrentQueue<(HttpMethod Method, string Path, string Query, string? Body)> Requests { get; } = new();

    public List<JsonObject> Products { get; } = new()
    {
        Product(1, "pro", "Pro Plan", 29900),
        Product(2, "basic", "Basic Plan", 2900),
        Product(3, "legacy", "Retired Plan", 1000, archived: true)
    };

    /// <summary>Products returned on every page regardless of the page number (to exercise the page cap).</summary>
    public bool RepeatProductsOnEveryPage { get; set; }

    /// <summary>When set, POST /customers.json records the customer and then drops the connection.</summary>
    public bool DropConnectionAfterCreatingCustomer { get; set; }

    /// <summary>When set, POST /subscriptions.json records the subscription and then drops the connection.</summary>
    public bool DropConnectionAfterCreatingSubscription { get; set; }

    /// <summary>When set, POST /subscriptions.json drops the connection without creating anything.</summary>
    public bool DropConnectionBeforeCreatingSubscription { get; set; }

    /// <summary>When set, subscription lookups answer 404 even for subscriptions that exist (a lagging read).</summary>
    public bool HideSubscriptionsFromLookup { get; set; }

    /// <summary>When set, POST /subscriptions.json answers 422 with these errors.</summary>
    public string[]? RejectSubscriptionWith { get; set; }

    /// <summary>When set, every call answers with this status (e.g. 401 for bad credentials).</summary>
    public HttpStatusCode? FailEverythingWith { get; set; }

    public int Count(HttpMethod method, string path) => Requests.Count(r => r.Method == method && r.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        Requests.Enqueue((request.Method, path, request.RequestUri.Query, body));

        if (FailEverythingWith is HttpStatusCode failure)
            return Json(failure, "{\"errors\":[\"failure\"]}");

        if (request.Method == HttpMethod.Get && path.StartsWith("/product_families/") && path.EndsWith("/products.json"))
        {
            var page = int.Parse(query["page"] ?? "1");
            var perPage = int.Parse(query["per_page"] ?? "20");
            var pageItems = RepeatProductsOnEveryPage
                ? Enumerable.Range(0, perPage).Select(i => Products[i % Products.Count])
                : page == 1 ? Products : Enumerable.Empty<JsonObject>();
            return Json(HttpStatusCode.OK, new JsonArray(pageItems.Select(p => (JsonNode)new JsonObject { ["product"] = p.DeepClone() }).ToArray()).ToJsonString());
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            var reference = query["reference"]!;
            return Customers.TryGetValue(reference, out var id)
                ? Json(HttpStatusCode.OK, new JsonObject { ["customer"] = Customer(id, reference) }.ToJsonString())
                : Json(HttpStatusCode.NotFound, "{\"errors\":[\"Not Found\"]}");
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            var customer = JsonNode.Parse(body!)!["customer"]!;
            var reference = (string)customer["reference"]!;
            if (Customers.ContainsKey(reference))
                return Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"Reference must be unique\"]}");
            var id = Interlocked.Increment(ref _nextId);
            Customers[reference] = id;
            if (DropConnectionAfterCreatingCustomer)
                throw new HttpRequestException("connection reset after the write landed");
            return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = Customer(id, reference) }.ToJsonString());
        }

        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            var reference = query["reference"]!;
            return !HideSubscriptionsFromLookup && Subscriptions.TryGetValue(reference, out var subscription)
                ? Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = subscription.DeepClone() }.ToJsonString())
                : Json(HttpStatusCode.NotFound, "");
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            if (DropConnectionBeforeCreatingSubscription)
                throw new HttpRequestException("connection reset");
            if (RejectSubscriptionWith is not null)
                return Json(HttpStatusCode.UnprocessableEntity, JsonSerializer.Serialize(new { errors = RejectSubscriptionWith }));

            var input = JsonNode.Parse(body!)!["subscription"]!;
            var handle = (string)input["product_handle"]!;
            var product = Products.First(p => (string)p["handle"]! == handle);
            var subscription = new JsonObject
            {
                ["id"] = Interlocked.Increment(ref _nextId),
                ["state"] = "active",
                ["reference"] = (string?)input["reference"],
                ["customer"] = new JsonObject { ["id"] = (int)input["customer_id"]! },
                ["product_price_in_cents"] = (long)product["price_in_cents"]!,
                ["currency"] = "USD",
                ["current_period_ends_at"] = "2026-11-20T10:00:00+00:00",
                ["next_assessment_at"] = "2026-11-20T10:00:00+00:00",
                ["created_at"] = "2026-10-20T10:00:00+00:00",
                ["product"] = product.DeepClone()
            };
            Subscriptions[(string)input["reference"]!] = subscription;

            if (DropConnectionAfterCreatingSubscription)
                throw new HttpRequestException("connection reset after the write landed");

            return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = subscription.DeepClone() }.ToJsonString());
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            var id = int.Parse(path.Split('/')[2]);
            var mine = Subscriptions.Values.Where(s => (int)s["customer"]!["id"]! == id)
                .Select(s => (JsonNode)new JsonObject { ["subscription"] = s.DeepClone() }).ToArray();
            return Json(HttpStatusCode.OK, new JsonArray(mine).ToJsonString());
        }

        return Json(HttpStatusCode.NotFound, "{\"errors\":[\"unexpected route\"]}");
    }

    private static JsonObject Customer(int id, string reference) => new()
    {
        ["id"] = id,
        ["reference"] = reference,
        ["email"] = "someone@example.com"
    };

    private static JsonObject Product(int id, string handle, string name, long priceInCents, bool archived = false)
    {
        var product = new JsonObject
        {
            ["id"] = id,
            ["handle"] = handle,
            ["name"] = name,
            ["price_in_cents"] = priceInCents,
            ["interval"] = 1,
            ["interval_unit"] = "month",
            ["require_credit_card"] = false
        };
        if (archived)
            product["archived_at"] = "2025-01-01T00:00:00+00:00";
        return product;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
