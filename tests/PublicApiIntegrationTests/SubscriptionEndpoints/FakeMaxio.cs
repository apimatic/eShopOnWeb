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
/// In-process stand-in for the Maxio API, plugged in as the primary handler of the SDK's HttpClient.
/// Models just the routes the integration calls; failures are injected through <see cref="Intercept"/>.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private readonly object _lock = new();
    private readonly Dictionary<string, JsonObject> _customersByReference = new(StringComparer.Ordinal);
    private readonly List<JsonObject> _subscriptions = new();
    private int _nextId = 1000;

    public record Call(HttpMethod Method, string Path, string Query, string? Body, string Host = "");

    public List<Call> Calls { get; } = new();

    /// <summary>
    /// Runs before routing. Return a response to short-circuit, throw to simulate a transport failure,
    /// or return null to let the fake handle the request normally.
    /// </summary>
    public Func<Call, FakeMaxio, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>Delay before answering create calls — widens the race window for concurrency tests.</summary>
    public TimeSpan CreateDelay { get; set; } = TimeSpan.Zero;

    public JsonArray Products { get; set; } = new();

    public int Count(HttpMethod method, string path) => Calls.Count(c => c.Method == method && c.Path == path);

    public void Reset()
    {
        Intercept = null;
        CreateDelay = TimeSpan.Zero;
    }

    public IReadOnlyList<JsonObject> SubscriptionsFor(int customerId)
    {
        lock (_lock)
        {
            return _subscriptions.Where(s => (int)s["customer"]!["id"]! == customerId).ToList();
        }
    }

    public JsonObject? CustomerByReference(string reference)
    {
        lock (_lock)
        {
            return _customersByReference.TryGetValue(reference, out var c) ? c : null;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var call = new Call(request.Method, Uri.UnescapeDataString(request.RequestUri!.AbsolutePath), request.RequestUri.Query, body, request.RequestUri.Host);
        lock (_lock)
        {
            Calls.Add(call);
        }

        var intercepted = Intercept?.Invoke(call, this);
        if (intercepted is not null)
        {
            return intercepted;
        }

        if (request.Method == HttpMethod.Post && CreateDelay > TimeSpan.Zero)
        {
            await Task.Delay(CreateDelay, cancellationToken);
        }

        return Route(call);
    }

    /// <summary>Applies a request to the fake's state (also usable from an interceptor before it fails the connection).</summary>
    public HttpResponseMessage Route(Call call)
    {
        lock (_lock)
        {
            var query = ParseQuery(call.Query);

            if (call.Method == HttpMethod.Get && call.Path.StartsWith("/product_families/", StringComparison.Ordinal) && call.Path.EndsWith("/products.json", StringComparison.Ordinal))
            {
                var page = int.Parse(query.GetValueOrDefault("page", "1"));
                return Json(HttpStatusCode.OK, page == 1 ? Products.DeepClone() : new JsonArray());
            }

            if (call.Method == HttpMethod.Get && call.Path == "/customers/lookup.json")
            {
                return _customersByReference.TryGetValue(query["reference"], out var customer)
                    ? Json(HttpStatusCode.OK, new JsonObject { ["customer"] = customer.DeepClone() })
                    : Json(HttpStatusCode.NotFound, new JsonObject { ["errors"] = new JsonArray("Not Found") });
            }

            if (call.Method == HttpMethod.Post && call.Path == "/customers.json")
            {
                var input = JsonNode.Parse(call.Body!)!["customer"]!;
                var reference = (string)input["reference"]!;
                if (_customersByReference.ContainsKey(reference))
                {
                    return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Reference must be unique.") });
                }

                var customer = new JsonObject
                {
                    ["id"] = _nextId++,
                    ["reference"] = reference,
                    ["email"] = (string?)input["email"],
                    ["first_name"] = (string?)input["first_name"],
                    ["last_name"] = (string?)input["last_name"]
                };
                _customersByReference[reference] = customer;
                return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = customer.DeepClone() });
            }

            if (call.Method == HttpMethod.Get && call.Path.StartsWith("/customers/", StringComparison.Ordinal) && call.Path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
            {
                var customerId = int.Parse(call.Path.Split('/')[2]);
                var list = new JsonArray(_subscriptions
                    .Where(s => (int)s["customer"]!["id"]! == customerId)
                    .Select(s => (JsonNode)new JsonObject { ["subscription"] = s.DeepClone() })
                    .ToArray());
                return Json(HttpStatusCode.OK, list);
            }

            if (call.Method == HttpMethod.Post && call.Path == "/subscriptions.json")
            {
                var input = JsonNode.Parse(call.Body!)!["subscription"]!;
                var handle = (string)input["product_handle"]!;
                var product = Products.Select(p => p!["product"]!).FirstOrDefault(p => (string?)p["handle"] == handle);
                if (product is null)
                {
                    return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Product not found") });
                }

                var customerId = (int)input["customer_id"]!;
                var now = DateTimeOffset.UtcNow;
                var subscription = new JsonObject
                {
                    ["id"] = _nextId++,
                    ["state"] = "active",
                    ["reference"] = (string?)input["reference"],
                    ["payment_collection_method"] = (string?)input["payment_collection_method"],
                    ["product_price_in_cents"] = (long)product["price_in_cents"]!,
                    ["currency"] = "USD",
                    ["created_at"] = now.ToString("O"),
                    ["activated_at"] = now.ToString("O"),
                    ["current_period_ends_at"] = now.AddMonths(1).ToString("O"),
                    ["next_assessment_at"] = now.AddMonths(1).ToString("O"),
                    ["customer"] = new JsonObject { ["id"] = customerId },
                    ["product"] = product.DeepClone()
                };
                _subscriptions.Add(subscription);
                return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = subscription.DeepClone() });
            }

            if (call.Method == HttpMethod.Get && call.Path == "/subscriptions/lookup.json")
            {
                var found = _subscriptions.FirstOrDefault(s => (string?)s["reference"] == query.GetValueOrDefault("reference"));
                return found is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = found.DeepClone() });
            }

            return Json(HttpStatusCode.NotFound, new JsonObject { ["errors"] = new JsonArray($"No fake route for {call.Method} {call.Path}") });
        }
    }

    public static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    public static JsonObject Product(int id, string handle, long priceInCents, bool requireCard = false, bool archived = false) => new()
    {
        ["product"] = new JsonObject
        {
            ["id"] = id,
            ["handle"] = handle,
            ["name"] = handle + " plan",
            ["price_in_cents"] = priceInCents,
            ["interval"] = 1,
            ["interval_unit"] = "month",
            ["require_credit_card"] = requireCard,
            ["archived_at"] = archived ? "2024-01-01T00:00:00Z" : null
        }
    };

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : string.Empty);

    public static string ReferenceSentIn(Call call) =>
        (string)JsonNode.Parse(call.Body!)!["subscription"]!["reference"]!;
}
