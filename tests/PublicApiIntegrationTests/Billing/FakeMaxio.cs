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
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Logging.Abstractions;

namespace PublicApiIntegrationTests.Billing;

/// <summary>
/// An in-process stand-in for the Maxio API, plugged in at the SDK's HttpClient seam. It keeps a small amount
/// of state (products, customers by reference, subscriptions) so flows behave like the real thing, records
/// every request (with its body, read while it is still readable), and lets a test intercept any request.
/// No network traffic ever leaves the process.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    public const string FamilyHandle = "test-family";
    public const string BaseUrl = "https://maxio.test";
    public const string SitePath = "/site.json";

    private int _nextCustomerId = 1000;
    private int _nextSubscriptionId = 5000;

    public List<JsonObject> Products { get; } = new();
    public ConcurrentDictionary<string, int> CustomersByReference { get; } = new();
    public ConcurrentDictionary<int, JsonObject> Subscriptions { get; } = new();
    public ConcurrentQueue<(HttpMethod Method, string Path, string Query, string? Body)> Requests { get; } = new();

    /// <summary>
    /// The site's <c>relationship_invoicing_enabled</c> flag served on GET /site.json. Defaults to true
    /// (Relationship Invoicing); null omits the field (an indeterminate architecture).
    /// </summary>
    public bool? RelationshipInvoicingEnabled { get; set; } = true;

    /// <summary>Runs before the default behaviour; return a response (or throw) to take over a request, or null to fall through.</summary>
    public Func<HttpRequestMessage, string, Task<HttpResponseMessage?>>? Intercept { get; set; }

    public static FakeMaxio WithDefaultPlans()
    {
        var fake = new FakeMaxio();
        fake.AddProduct("pro-test", "Pro (test)", 29900);
        fake.AddProduct("basic-test", "Basic (test)", 2900);
        return fake;
    }

    public void AddProduct(string handle, string name, long priceInCents, bool archived = false, bool requireCreditCard = false)
    {
        var product = new JsonObject
        {
            ["id"] = Products.Count + 1,
            ["handle"] = handle,
            ["name"] = name,
            ["description"] = name + " plan",
            ["price_in_cents"] = priceInCents,
            ["interval"] = 1,
            ["interval_unit"] = "month",
            ["require_credit_card"] = requireCreditCard
        };
        if (archived) product["archived_at"] = "2026-01-01T00:00:00Z";
        Products.Add(product);
    }

    public int Count(HttpMethod method, string path) => Requests.Count(r => r.Method == method && r.Path == path);

    public MaxioSettings Settings() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        ProductFamilyHandle = FamilyHandle,
        BaseUrl = BaseUrl
    };

    public MaxioAdvancedBillingClient CreateClient() =>
        new(new HttpClient(this, disposeHandler: false),
            MaxioConfigurationExtensions.CreateClientOptions(Settings(), NullLoggerFactory.Instance, TimeProvider.System));

    /// <summary>Registers a subscription directly, as if it had been created at Maxio.</summary>
    public JsonObject AddSubscription(int customerId, string productHandle, string? reference)
    {
        var id = Interlocked.Increment(ref _nextSubscriptionId);
        var product = Products.FirstOrDefault(p => (string?)p["handle"] == productHandle);
        var subscription = new JsonObject
        {
            ["id"] = id,
            ["state"] = "active",
            ["reference"] = reference,
            ["product_price_in_cents"] = product?["price_in_cents"]?.GetValue<long>() ?? 0,
            ["currency"] = "USD",
            ["created_at"] = "2026-10-20T10:00:00Z",
            ["current_period_ends_at"] = "2026-11-20T10:00:00Z",
            ["next_assessment_at"] = "2026-11-20T10:00:00Z",
            ["customer"] = new JsonObject { ["id"] = customerId },
            ["product"] = product?.DeepClone()
        };
        Subscriptions[id] = subscription;
        return subscription;
    }

    public int AddCustomer(string reference)
    {
        var id = Interlocked.Increment(ref _nextCustomerId);
        CustomersByReference[reference] = id;
        return id;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Requests.Enqueue((request.Method, path, request.RequestUri.Query, body));

        if (Intercept is not null)
        {
            var intercepted = await Intercept(request, body ?? string.Empty);
            if (intercepted is not null) return intercepted;
        }

        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);

        if (request.Method == HttpMethod.Get && path == SitePath)
        {
            var site = new JsonObject { ["id"] = 1, ["name"] = "Test site", ["subdomain"] = "test-site", ["test"] = true };
            if (RelationshipInvoicingEnabled is bool enabled) site["relationship_invoicing_enabled"] = enabled;
            return Json(HttpStatusCode.OK, new JsonObject { ["site"] = site });
        }

        if (request.Method == HttpMethod.Get && path == $"/product_families/handle:{FamilyHandle}/products.json")
        {
            var page = int.Parse(query["page"] ?? "1");
            var perPage = int.Parse(query["per_page"] ?? "20");
            var items = new JsonArray(Products.Skip((page - 1) * perPage).Take(perPage)
                .Select(p => (JsonNode)new JsonObject { ["product"] = p.DeepClone() }).ToArray());
            return Json(HttpStatusCode.OK, items);
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            var reference = query["reference"] ?? string.Empty;
            return CustomersByReference.TryGetValue(reference, out var id)
                ? Json(HttpStatusCode.OK, CustomerJson(id, reference))
                : Json(HttpStatusCode.NotFound, new JsonObject { ["errors"] = new JsonArray("Customer not found") });
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            var reference = CreatedCustomerReference(body);
            if (CustomersByReference.ContainsKey(reference))
            {
                return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Reference must be unique") });
            }

            return Json(HttpStatusCode.Created, CustomerJson(AddCustomer(reference), reference));
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/", StringComparison.Ordinal) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
        {
            var customerId = int.Parse(path.Split('/')[2]);
            var items = new JsonArray(Subscriptions.Values
                .Where(s => s["customer"]!["id"]!.GetValue<int>() == customerId)
                .OrderBy(s => s["id"]!.GetValue<int>())
                .Select(s => (JsonNode)new JsonObject { ["subscription"] = s.DeepClone() }).ToArray());
            return Json(HttpStatusCode.OK, items);
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            var (customerId, productHandle, reference) = CreatedSubscription(body);
            return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = AddSubscription(customerId, productHandle, reference).DeepClone() });
        }

        return Json(HttpStatusCode.NotFound, new JsonObject { ["errors"] = new JsonArray($"No fake route for {request.Method} {path}") });
    }

    public static string CreatedCustomerReference(string? body) =>
        JsonNode.Parse(body!)!["customer"]!["reference"]!.GetValue<string>();

    /// <summary>The <c>payment_collection_method</c> sent in a CreateSubscription body, or null when it was not sent.</summary>
    public static string? CreatedSubscriptionCollectionMethod(string? body) =>
        (string?)JsonNode.Parse(body!)!["subscription"]!["payment_collection_method"];

    public static (int CustomerId, string ProductHandle, string? Reference) CreatedSubscription(string? body)
    {
        var subscription = JsonNode.Parse(body!)!["subscription"]!;
        return (subscription["customer_id"]!.GetValue<int>(), subscription["product_handle"]!.GetValue<string>(), (string?)subscription["reference"]);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) =>
        new(status) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };

    private static JsonObject CustomerJson(int id, string reference) => new()
    {
        ["customer"] = new JsonObject
        {
            ["id"] = id,
            ["reference"] = reference,
            ["first_name"] = "test",
            ["last_name"] = "Customer",
            ["email"] = "test@example.test"
        }
    };
}
