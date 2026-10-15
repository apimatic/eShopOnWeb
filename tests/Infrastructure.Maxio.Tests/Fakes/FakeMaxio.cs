using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Tests.Fakes;

/// <summary>
/// An in-memory stand-in for the Maxio Advanced Billing HTTP API, used as the SDK client's
/// HttpMessageHandler. Implements only the routes this integration calls. <see cref="Intercept"/>
/// lets a test replace any response (or throw, to simulate a transport failure).
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private readonly object _gate = new();
    private int _nextId = 1000;

    public List<JsonObject> Products { get; } = new();
    public Dictionary<int, JsonObject> Customers { get; } = new();
    public Dictionary<int, JsonObject> Subscriptions { get; } = new();

    /// <summary>Every request as "METHOD /path", in order.</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>Request bodies keyed by "METHOD /path", captured before the SDK disposes them.</summary>
    public ConcurrentQueue<(string Call, string Body)> Bodies { get; } = new();

    /// <summary>Return a response (or throw) to override the fake; return null to fall through.</summary>
    public Func<HttpRequestMessage, string, string, HttpResponseMessage?>? Intercept { get; set; }

    public Uri? LastRequestUri { get; private set; }

    public int Count(string call) => Calls.Count(c => c == call);

    public static FakeMaxio WithDefaultCatalog()
    {
        var fake = new FakeMaxio();
        fake.AddProduct("eshop-pro", "Pro Plan", 29900);
        fake.AddProduct("basic-plan", "Basic Plan", 2900);
        return fake;
    }

    public JsonObject AddProduct(string handle, string name, long priceInCents, bool archived = false, bool requireCard = false)
    {
        var product = new JsonObject
        {
            ["id"] = Interlocked.Increment(ref _nextId),
            ["handle"] = handle,
            ["name"] = name,
            ["price_in_cents"] = priceInCents,
            ["interval"] = 1,
            ["interval_unit"] = "month",
            ["require_credit_card"] = requireCard,
            ["archived_at"] = archived ? "2026-01-01T00:00:00Z" : null,
        };
        Products.Add(product);
        return product;
    }

    public JsonObject AddCustomer(string reference)
    {
        var id = Interlocked.Increment(ref _nextId);
        var customer = new JsonObject { ["id"] = id, ["reference"] = reference, ["first_name"] = "x", ["last_name"] = "y", ["email"] = "x@example.com" };
        lock (_gate) { Customers[id] = customer; }
        return customer;
    }

    public JsonObject AddSubscription(int customerId, string productHandle, string? reference, string state = "active")
    {
        var id = Interlocked.Increment(ref _nextId);
        var product = Products.First(p => (string?)p["handle"] == productHandle);
        var subscription = new JsonObject
        {
            ["id"] = id,
            ["state"] = state,
            ["reference"] = reference,
            ["currency"] = "USD",
            ["product_price_in_cents"] = (long)product["price_in_cents"]!,
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["activated_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["current_period_ends_at"] = DateTimeOffset.UtcNow.AddMonths(1).ToString("O"),
            ["next_assessment_at"] = DateTimeOffset.UtcNow.AddMonths(1).ToString("O"),
            ["product"] = product.DeepClone(),
            ["customer"] = new JsonObject { ["id"] = customerId },
        };
        lock (_gate) { Subscriptions[id] = subscription; }
        return subscription;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        var path = request.RequestUri!.AbsolutePath;
        var call = $"{request.Method.Method} {NormalizePath(path)}";
        Calls.Enqueue(call);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 0)
        {
            Bodies.Enqueue((call, body));
        }

        var intercepted = Intercept?.Invoke(request, call, body);
        if (intercepted is not null)
        {
            intercepted.RequestMessage = request;
            return intercepted;
        }

        var response = Route(request, path, body);
        response.RequestMessage = request;
        return response;
    }

    /// <summary>Replaces numeric ids with {id} so calls can be counted by route.</summary>
    public static string NormalizePath(string path) =>
        string.Join('/', path.Split('/').Select(s => s.Length > 0 && s.All(char.IsDigit) ? "{id}" : s.StartsWith("handle:", StringComparison.Ordinal) || s.StartsWith("handle%3A", StringComparison.OrdinalIgnoreCase) ? "{family}" : s));

    /// <summary>Lets an intercept apply the request to the fake's state (e.g. create, then fail the connection).</summary>
    public HttpResponseMessage Apply(HttpRequestMessage request, string body) => Route(request, request.RequestUri!.AbsolutePath, body);

    private HttpResponseMessage Route(HttpRequestMessage request, string path, string body)
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var segments = path.Trim('/').Split('/');

        lock (_gate)
        {
            if (request.Method == HttpMethod.Get && segments is ["product_families", _, "products.json"])
            {
                var page = int.Parse(query["page"] ?? "1");
                var perPage = int.Parse(query["per_page"] ?? "20");
                var items = Products.Skip((page - 1) * perPage).Take(perPage).Select(p => new JsonObject { ["product"] = p.DeepClone() });
                return Json(HttpStatusCode.OK, new JsonArray(items.ToArray<JsonNode?>()));
            }

            if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                var match = Customers.Values.FirstOrDefault(c => (string?)c["reference"] == query["reference"]);
                return match is null
                    ? Json(HttpStatusCode.NotFound, new JsonObject { ["errors"] = new JsonArray("Not Found") })
                    : Json(HttpStatusCode.OK, new JsonObject { ["customer"] = match.DeepClone() });
            }

            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                var input = JsonNode.Parse(body)!["customer"]!.AsObject();
                var reference = (string?)input["reference"];
                if (reference is not null && Customers.Values.Any(c => (string?)c["reference"] == reference))
                {
                    return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Reference: must be unique - that value has been taken.") });
                }
                var customer = AddCustomer(reference ?? Guid.NewGuid().ToString());
                foreach (var key in new[] { "first_name", "last_name", "email" })
                {
                    customer[key] = input[key]?.DeepClone();
                }
                return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = customer.DeepClone() });
            }

            if (request.Method == HttpMethod.Get && segments is ["customers", var customerSegment, "subscriptions.json"])
            {
                var id = int.Parse(customerSegment);
                var items = Subscriptions.Values.Where(s => (int)s["customer"]!["id"]! == id).Select(s => new JsonObject { ["subscription"] = s.DeepClone() });
                return Json(HttpStatusCode.OK, new JsonArray(items.ToArray<JsonNode?>()));
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                var input = JsonNode.Parse(body)!["subscription"]!.AsObject();
                var customerId = (int)input["customer_id"]!;
                if (!Customers.ContainsKey(customerId))
                {
                    return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Customer: must be specified.") });
                }
                var subscription = AddSubscription(customerId, (string)input["product_handle"]!, (string?)input["reference"]);
                return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = subscription.DeepClone() });
            }

            if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
            {
                var match = Subscriptions.Values.FirstOrDefault(s => (string?)s["reference"] == query["reference"]);
                return match is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = match.DeepClone() });
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("unrouted: " + path) };
    }

    public static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) =>
        new(status) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };

    public static JsonElement ParseBody(string body) => JsonDocument.Parse(body).RootElement;
}
