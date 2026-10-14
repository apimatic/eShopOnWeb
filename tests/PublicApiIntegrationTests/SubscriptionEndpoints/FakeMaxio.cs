using System;
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
/// A stateful stand-in for the Maxio Advanced Billing API, plugged in as the primary handler of the SDK's
/// HttpClient. Records every request (body captured while still readable) and supports fault injection.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    public const string FamilyHandle = "test-family";

    private readonly object _lock = new();
    private readonly Dictionary<string, JsonObject> _customersByReference = new();
    private readonly Dictionary<int, JsonObject> _subscriptionsById = new();
    private int _nextId = 1000;

    public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body);

    public List<RecordedRequest> Requests { get; } = new();

    public List<JsonObject> Products { get; } = new()
    {
        Product(1, "pro", "Pro Plan", 29900),
        Product(2, "basic", "Basic Plan", 2900),
    };

    /// <summary>Runs before routing; return a response to override it, throw to simulate a transport failure, or null to continue.</summary>
    public Func<RecordedRequest, HttpResponseMessage?>? BeforeRoute { get; set; }

    /// <summary>Runs after routing (the fake has already acted); throw to simulate a connection dropped after Maxio acted.</summary>
    public Action<RecordedRequest>? AfterRoute { get; set; }

    /// <summary>Awaited before a subscription is created — lets a test hold the first create open.</summary>
    public Func<Task>? BeforeCreateSubscription { get; set; }

    /// <summary>When false, subscription lookups by reference return 404 even if the subscription exists.</summary>
    public bool SubscriptionLookupFindsCreated { get; set; } = true;

    public int Count(HttpMethod method, string path)
    {
        lock (_lock)
        {
            return Requests.Count(r => r.Method == method && r.Path == path);
        }
    }

    public int CustomerCount { get { lock (_lock) { return _customersByReference.Count; } } }

    public int SubscriptionCount { get { lock (_lock) { return _subscriptionsById.Count; } } }

    public static JsonObject Product(int id, string handle, string name, long priceInCents) => new()
    {
        ["id"] = id,
        ["handle"] = handle,
        ["name"] = name,
        ["price_in_cents"] = priceInCents,
        ["interval"] = 1,
        ["interval_unit"] = "month",
        ["product_family"] = new JsonObject { ["handle"] = FamilyHandle },
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, Uri.UnescapeDataString(request.RequestUri!.AbsolutePath),
            Uri.UnescapeDataString(request.RequestUri.Query), body);
        lock (_lock)
        {
            Requests.Add(recorded);
        }

        var overridden = BeforeRoute?.Invoke(recorded);
        if (overridden is not null)
        {
            return overridden;
        }

        if (recorded.Method == HttpMethod.Post && recorded.Path == "/subscriptions.json" && BeforeCreateSubscription is not null)
        {
            await BeforeCreateSubscription();
        }

        var response = Route(recorded);
        AfterRoute?.Invoke(recorded);
        return response;
    }

    private HttpResponseMessage Route(RecordedRequest r)
    {
        lock (_lock)
        {
            var query = ParseQuery(r.Query);

            if (r.Method == HttpMethod.Get && r.Path == $"/product_families/handle:{FamilyHandle}/products.json")
            {
                var page = int.Parse(query.GetValueOrDefault("page", "1"));
                var perPage = int.Parse(query.GetValueOrDefault("per_page", "20"));
                var items = Products.Skip((page - 1) * perPage).Take(perPage)
                    .Select(p => new JsonObject { ["product"] = p.DeepClone() });
                return Json(HttpStatusCode.OK, new JsonArray(items.ToArray<JsonNode?>()));
            }

            if (r.Method == HttpMethod.Get && r.Path == "/customers/lookup.json")
            {
                return _customersByReference.TryGetValue(query.GetValueOrDefault("reference", ""), out var customer)
                    ? Json(HttpStatusCode.OK, new JsonObject { ["customer"] = customer.DeepClone() })
                    : Empty(HttpStatusCode.NotFound);
            }

            if (r.Method == HttpMethod.Post && r.Path == "/customers.json")
            {
                var input = JsonNode.Parse(r.Body!)!["customer"]!.AsObject();
                var reference = input["reference"]?.GetValue<string>() ?? $"none-{_nextId}";
                if (_customersByReference.ContainsKey(reference))
                {
                    return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Reference: must be unique") });
                }
                var customer = new JsonObject
                {
                    ["id"] = ++_nextId,
                    ["reference"] = reference,
                    ["email"] = input["email"]?.GetValue<string>(),
                    ["first_name"] = input["first_name"]?.GetValue<string>(),
                    ["last_name"] = input["last_name"]?.GetValue<string>(),
                };
                _customersByReference[reference] = customer;
                return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = customer.DeepClone() });
            }

            if (r.Method == HttpMethod.Post && r.Path == "/subscriptions.json")
            {
                var input = JsonNode.Parse(r.Body!)!["subscription"]!.AsObject();
                var handle = input["product_handle"]!.GetValue<string>();
                var customerId = input["customer_id"]!.GetValue<int>();
                var product = Products.First(p => p["handle"]!.GetValue<string>() == handle);
                var customer = _customersByReference.Values.First(c => c["id"]!.GetValue<int>() == customerId);
                var id = ++_nextId;
                var subscription = new JsonObject
                {
                    ["id"] = id,
                    ["state"] = "active",
                    ["reference"] = input["reference"]?.GetValue<string>(),
                    ["product_price_in_cents"] = product["price_in_cents"]!.GetValue<long>(),
                    ["currency"] = "USD",
                    ["payment_collection_method"] = input["payment_collection_method"]?.GetValue<string>() ?? "automatic",
                    ["current_period_ends_at"] = "2026-11-14T00:00:00Z",
                    ["activated_at"] = "2026-10-14T00:00:00Z",
                    ["created_at"] = "2026-10-14T00:00:00Z",
                    ["customer"] = customer.DeepClone(),
                    ["product"] = product.DeepClone(),
                };
                _subscriptionsById[id] = subscription;
                return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = subscription.DeepClone() });
            }

            if (r.Method == HttpMethod.Get && r.Path == "/subscriptions/lookup.json")
            {
                var reference = query.GetValueOrDefault("reference", "");
                var match = SubscriptionLookupFindsCreated
                    ? _subscriptionsById.Values.FirstOrDefault(s => s["reference"]?.GetValue<string>() == reference)
                    : null;
                return match is null
                    ? Empty(HttpStatusCode.NotFound)
                    : Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = match.DeepClone() });
            }

            if (r.Method == HttpMethod.Get && r.Path.StartsWith("/subscriptions/") && r.Path.EndsWith(".json"))
            {
                var id = int.Parse(r.Path["/subscriptions/".Length..^".json".Length]);
                return _subscriptionsById.TryGetValue(id, out var s)
                    ? Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = s.DeepClone() })
                    : Empty(HttpStatusCode.NotFound);
            }

            if (r.Method == HttpMethod.Get && r.Path.StartsWith("/customers/") && r.Path.EndsWith("/subscriptions.json"))
            {
                var customerId = int.Parse(r.Path.Split('/')[2]);
                var list = _subscriptionsById.Values
                    .Where(s => s["customer"]!["id"]!.GetValue<int>() == customerId)
                    .Select(s => new JsonObject { ["subscription"] = s.DeepClone() });
                return Json(HttpStatusCode.OK, new JsonArray(list.ToArray<JsonNode?>()));
            }

            return Empty(HttpStatusCode.NotFound);
        }
    }

    public void SetSubscriptionState(int id, string state)
    {
        lock (_lock)
        {
            _subscriptionsById[id]["state"] = state;
        }
    }

    public static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) => new(status)
    {
        Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage Empty(HttpStatusCode status) => new(status)
    {
        Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
    };

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .GroupBy(parts => parts[0])
            .ToDictionary(g => g.Key, g => g.First().Length > 1 ? g.First()[1] : string.Empty);
}

public static class JsonTestExtensions
{
    public static JsonNode? ParseBody(this FakeMaxio.RecordedRequest request) =>
        request.Body is null ? null : JsonNode.Parse(request.Body);

    public static T? Deserialize<T>(this string json) =>
        JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
}
