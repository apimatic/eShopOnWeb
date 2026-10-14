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
/// An in-process stand-in for the Maxio API, plugged in as the primary handler of the SDK's HttpClient.
/// It keeps customers and subscriptions by reference so lookups behave like the provider, and can fail a
/// connection before or after it "acts" on a write.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    public enum WriteFailure
    {
        None,
        /// <summary>The provider never receives the write; the connection fails.</summary>
        DropBeforeActing,
        /// <summary>The provider performs the write, then the connection fails before the response arrives.</summary>
        DropAfterActing,
        /// <summary>The provider refuses the write with 422.</summary>
        Reject
    }

    private readonly ConcurrentDictionary<string, JsonObject> _customersByReference = new();
    private readonly ConcurrentDictionary<string, JsonObject> _subscriptionsByReference = new();
    private int _nextCustomerId = 500;
    private int _nextSubscriptionId = 9000;

    public string FamilyHandle { get; init; } = "test-family";

    public List<JsonObject> Products { get; } = new()
    {
        Product(1, "basic", "Basic", 2900),
        Product(2, "pro", "Pro", 29900),
    };

    /// <summary>When set, every products page is full, so the client has to stop at its own cap.</summary>
    public bool EndlessProducts { get; set; }

    public WriteFailure CustomerCreateFailure { get; set; }
    public WriteFailure SubscriptionCreateFailure { get; set; }

    /// <summary>Holds subscription creates open so concurrent requests overlap.</summary>
    public TimeSpan SubscriptionCreateDelay { get; set; }

    public ConcurrentQueue<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

    public int Count(HttpMethod method, string pathSuffix) =>
        Requests.Count(r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.Ordinal));

    public void SeedCustomer(string reference, int id) =>
        _customersByReference[reference] = new JsonObject { ["id"] = id, ["reference"] = reference, ["email"] = "seeded@example.com" };

    public static JsonObject Product(int id, string handle, string name, long priceInCents, bool archived = false, bool requireCreditCard = false) => new()
    {
        ["id"] = id,
        ["handle"] = handle,
        ["name"] = name,
        ["price_in_cents"] = priceInCents,
        ["interval"] = 1,
        ["interval_unit"] = "month",
        ["archived_at"] = archived ? "2026-01-01T00:00:00Z" : null,
        ["require_credit_card"] = requireCreditCard,
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Requests.Enqueue((request.Method, path, body));
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);

        if (request.Method == HttpMethod.Get && path == $"/product_families/handle:{FamilyHandle}/products.json")
        {
            var perPage = int.Parse(query["per_page"] ?? "20");
            var page = int.Parse(query["page"] ?? "1");
            IEnumerable<JsonObject> items = EndlessProducts
                ? Enumerable.Range(0, perPage).Select(i => Product(100000 + page * 1000 + i, $"p{page}-{i}", $"P{page}-{i}", 100))
                : Products.Skip((page - 1) * perPage).Take(perPage);
            return Json(HttpStatusCode.OK, new JsonArray(items.Select(p => (JsonNode)new JsonObject { ["product"] = p.DeepClone() }).ToArray()));
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/product_families/", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("\"Not Found\"", Encoding.UTF8, "application/json") };
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            return _customersByReference.TryGetValue(query["reference"] ?? "", out var customer)
                ? Json(HttpStatusCode.OK, new JsonObject { ["customer"] = customer.DeepClone() })
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            var input = JsonNode.Parse(body!)!["customer"]!.AsObject();
            var reference = (string)input["reference"]!;
            if (CustomerCreateFailure == WriteFailure.DropBeforeActing)
            {
                throw new HttpRequestException("connection reset (before acting)");
            }

            if (_customersByReference.ContainsKey(reference))
            {
                return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Reference must be unique") });
            }

            var customer = new JsonObject { ["id"] = Interlocked.Increment(ref _nextCustomerId), ["reference"] = reference, ["email"] = (string?)input["email"] };
            _customersByReference[reference] = customer;
            if (CustomerCreateFailure == WriteFailure.DropAfterActing)
            {
                throw new HttpRequestException("connection reset (after acting)");
            }

            return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = customer.DeepClone() });
        }

        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            return _subscriptionsByReference.TryGetValue(query["reference"] ?? "", out var subscription)
                ? Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = subscription.DeepClone() })
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            if (SubscriptionCreateDelay > TimeSpan.Zero)
            {
                await Task.Delay(SubscriptionCreateDelay, cancellationToken);
            }

            var input = JsonNode.Parse(body!)!["subscription"]!.AsObject();
            switch (SubscriptionCreateFailure)
            {
                case WriteFailure.DropBeforeActing:
                    throw new HttpRequestException("connection reset (before acting)");
                case WriteFailure.Reject:
                    return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Product is not available") });
            }

            var reference = (string)input["reference"]!;
            var product = Products.First(p => (string?)p["handle"] == (string?)input["product_handle"]);
            var customerId = (int)input["customer_id"]!;
            var subscription = new JsonObject
            {
                ["id"] = Interlocked.Increment(ref _nextSubscriptionId),
                ["state"] = "active",
                ["reference"] = reference,
                ["product_price_in_cents"] = (long)product["price_in_cents"]!,
                ["currency"] = "USD",
                ["current_period_ends_at"] = "2026-11-14T00:00:00Z",
                ["next_assessment_at"] = "2026-11-14T00:00:00Z",
                ["created_at"] = "2026-10-14T00:00:00Z",
                ["product"] = product.DeepClone(),
                ["customer"] = new JsonObject { ["id"] = customerId },
            };
            _subscriptionsByReference[reference] = subscription;
            if (SubscriptionCreateFailure == WriteFailure.DropAfterActing)
            {
                throw new HttpRequestException("connection reset (after acting)");
            }

            return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = subscription.DeepClone() });
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/", StringComparison.Ordinal) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
        {
            var customerId = int.Parse(path.Split('/')[2]);
            var items = _subscriptionsByReference.Values
                .Where(s => (int)s["customer"]!["id"]! == customerId)
                .Select(s => (JsonNode)new JsonObject { ["subscription"] = s.DeepClone() })
                .ToArray();
            return Json(HttpStatusCode.OK, new JsonArray(items));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}
