using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// An in-process stand-in for the Maxio Advanced Billing API, plugged in as the SDK's HttpClient handler so the
/// real SDK client (serialization, error mapping, retries) is exercised without network access.
/// Bodies use Maxio's wire names.
/// </summary>
public sealed class FakeMaxioHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private int _nextCustomerId = 5000;
    private int _nextSubscriptionId = 9000;

    public string ProductFamilyHandle { get; set; } = "test-family";

    public List<FakeProduct> Products { get; } = new()
    {
        new FakeProduct(101, "eshop-pro", "Pro Plan", 29900),
        new FakeProduct(102, "basic-plan", "Basic Plan", 2900),
        new FakeProduct(103, "old-plan", "Retired Plan", 1000, Archived: true)
    };

    public List<FakeCustomer> Customers { get; } = new();
    public List<FakeSubscription> Subscriptions { get; } = new();
    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>
    /// Runs before the built-in routes; return a response (or throw) to inject a fault, or null to fall through.
    /// </summary>
    public Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage?>>? Intercept { get; set; }

    public int Count(HttpMethod method, string path)
    {
        lock (_gate)
        {
            return Requests.Count(r => r.Method == method && r.Path == path);
        }
    }

    public FakeCustomer AddCustomer(string reference, string email)
    {
        lock (_gate)
        {
            var customer = new FakeCustomer(++_nextCustomerId, reference, email);
            Customers.Add(customer);
            return customer;
        }
    }

    public FakeSubscription AddSubscription(int customerId, string productHandle, string? reference, string state = "active")
    {
        lock (_gate)
        {
            var subscription = new FakeSubscription(++_nextSubscriptionId, customerId, productHandle, reference) { State = state };
            Subscriptions.Add(subscription);
            return subscription;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        lock (_gate)
        {
            Requests.Add(new RecordedRequest(request.Method, path, request.RequestUri, body));
        }

        if (Intercept is not null)
        {
            var injected = await Intercept(request, body, cancellationToken);
            if (injected is not null)
            {
                return injected;
            }
        }

        lock (_gate)
        {
            return Route(request.Method, path, HttpUtility.ParseQueryString(request.RequestUri.Query), body);
        }
    }

    private HttpResponseMessage Route(HttpMethod method, string path, System.Collections.Specialized.NameValueCollection query, string? body)
    {
        var segments = path.Trim('/').Split('/');

        if (method == HttpMethod.Get && segments is ["product_families", var family, "products.json"])
        {
            if (family != $"handle:{ProductFamilyHandle}")
            {
                return Json(HttpStatusCode.NotFound, "\"Not Found\"");
            }

            var page = int.Parse(query["page"] ?? "1");
            var perPage = int.Parse(query["per_page"] ?? "20");
            var items = Products.Skip((page - 1) * perPage).Take(perPage).Select(p => new JsonObject { ["product"] = ProductJson(p) });
            return Json(HttpStatusCode.OK, new JsonArray(items.ToArray<JsonNode?>()).ToJsonString());
        }

        if (method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            var customer = Customers.FirstOrDefault(c => c.Reference == query["reference"]);
            return customer is null
                ? Json(HttpStatusCode.NotFound, "{}")
                : Json(HttpStatusCode.OK, new JsonObject { ["customer"] = CustomerJson(customer) }.ToJsonString());
        }

        if (method == HttpMethod.Post && path == "/customers.json")
        {
            var input = JsonNode.Parse(body!)!["customer"]!;
            var reference = input["reference"]?.GetValue<string>();
            if (reference is not null && Customers.Any(c => c.Reference == reference))
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Reference must be unique."]}""");
            }

            var customer = new FakeCustomer(++_nextCustomerId, reference, input["email"]!.GetValue<string>());
            Customers.Add(customer);
            return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = CustomerJson(customer) }.ToJsonString());
        }

        if (method == HttpMethod.Get && segments is ["customers", var customerIdText, "subscriptions.json"])
        {
            var customerId = int.Parse(customerIdText);
            var items = Subscriptions.Where(s => s.CustomerId == customerId).Select(s => new JsonObject { ["subscription"] = SubscriptionJson(s) });
            return Json(HttpStatusCode.OK, new JsonArray(items.ToArray<JsonNode?>()).ToJsonString());
        }

        if (method == HttpMethod.Post && path == "/subscriptions.json")
        {
            var input = JsonNode.Parse(body!)!["subscription"]!;
            var handle = input["product_handle"]?.GetValue<string>();
            if (Products.All(p => p.Handle != handle))
            {
                return Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Product must be specified."]}""");
            }

            var subscription = new FakeSubscription(++_nextSubscriptionId, input["customer_id"]!.GetValue<int>(), handle!,
                input["reference"]?.GetValue<string>());
            Subscriptions.Add(subscription);
            return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = SubscriptionJson(subscription) }.ToJsonString());
        }

        if (method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            var subscription = Subscriptions.FirstOrDefault(s => s.Reference == query["reference"]);
            return subscription is null
                ? Json(HttpStatusCode.NotFound, "{}")
                : Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = SubscriptionJson(subscription) }.ToJsonString());
        }

        if (method == HttpMethod.Get && segments is ["subscriptions", var subscriptionFile] && subscriptionFile.EndsWith(".json"))
        {
            var id = int.Parse(subscriptionFile[..^5]);
            var subscription = Subscriptions.FirstOrDefault(s => s.Id == id);
            return subscription is null
                ? Json(HttpStatusCode.NotFound, "{}")
                : Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = SubscriptionJson(subscription) }.ToJsonString());
        }

        return Json(HttpStatusCode.NotFound, """{"errors":["unknown route"]}""");
    }

    public FakeSubscription CreateSubscriptionDirectly(string bodyJson)
    {
        var input = JsonNode.Parse(bodyJson)!["subscription"]!;
        return AddSubscription(input["customer_id"]!.GetValue<int>(), input["product_handle"]!.GetValue<string>(),
            input["reference"]?.GetValue<string>());
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static JsonObject ProductJson(FakeProduct p) => new()
    {
        ["id"] = p.Id,
        ["handle"] = p.Handle,
        ["name"] = p.Name,
        ["description"] = null,
        ["price_in_cents"] = p.PriceInCents,
        ["interval"] = 1,
        ["interval_unit"] = "month",
        ["archived_at"] = p.Archived ? "2026-01-01T00:00:00Z" : null
    };

    private static JsonObject CustomerJson(FakeCustomer c) => new()
    {
        ["id"] = c.Id,
        ["reference"] = c.Reference,
        ["email"] = c.Email
    };

    private JsonObject SubscriptionJson(FakeSubscription s)
    {
        var product = Products.First(p => p.Handle == s.ProductHandle);
        return new JsonObject
        {
            ["id"] = s.Id,
            ["state"] = s.State,
            ["reference"] = s.Reference,
            ["currency"] = "USD",
            ["product_price_in_cents"] = product.PriceInCents,
            ["created_at"] = "2026-10-06T10:00:00Z",
            ["current_period_ends_at"] = "2026-11-06T10:00:00Z",
            ["next_assessment_at"] = "2026-11-06T10:00:00Z",
            ["customer"] = new JsonObject { ["id"] = s.CustomerId },
            ["product"] = ProductJson(product)
        };
    }
}

public record FakeProduct(int Id, string Handle, string Name, long PriceInCents, bool Archived = false);

public record FakeCustomer(int Id, string? Reference, string Email);

public record FakeSubscription(int Id, int CustomerId, string ProductHandle, string? Reference)
{
    public string State { get; set; } = "active";
}

public record RecordedRequest(HttpMethod Method, string Path, Uri Uri, string? Body)
{
    public JsonNode? JsonBody => Body is null ? null : JsonNode.Parse(Body);
}

internal static class JsonExtensions
{
    public static string? Text(this JsonNode? node, string property) =>
        node?[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
