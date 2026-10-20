#nullable enable
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

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// An in-process stand-in for the Maxio API at the HttpClient seam. It keeps customers and
/// subscriptions so tests can assert what the provider actually received and holds.
/// </summary>
public class FakeMaxio : HttpMessageHandler
{
    private int _nextId = 1000;
    private readonly object _gate = new();

    public List<(string Handle, string Name, long PriceInCents)> Products { get; } = new()
    {
        ("eshop-pro", "Pro Plan", 29900),
        ("basic-plan", "Basic Plan", 2900)
    };

    public ConcurrentDictionary<string, int> CustomersByReference { get; } = new();
    public List<JsonObject> Subscriptions { get; } = new();

    public int CreateCustomerPosts;
    public int CreateSubscriptionPosts;

    /// <summary>Delay applied inside POST /subscriptions.json so concurrent callers overlap.</summary>
    public TimeSpan CreateSubscriptionDelay { get; set; } = TimeSpan.Zero;

    /// <summary>The provider acts on the next create-subscription, then the connection drops.</summary>
    public bool DropConnectionAfterCreatingSubscription { get; set; }

    /// <summary>The connection drops on the next create-subscription before the provider acts.</summary>
    public bool DropConnectionBeforeCreatingSubscription { get; set; }

    /// <summary>The provider acts on the next create-customer, then the connection drops.</summary>
    public bool DropConnectionAfterCreatingCustomer { get; set; }

    /// <summary>Errors returned (422) for the next create-subscription.</summary>
    public string[]? RejectNextSubscriptionWith { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        if (request.Method == HttpMethod.Get && path.EndsWith("/products.json"))
        {
            var page = int.Parse(query["page"] ?? "1");
            var products = page == 1
                ? Products.Select((p, i) => new JsonObject
                {
                    ["product"] = new JsonObject
                    {
                        ["id"] = i + 1,
                        ["handle"] = p.Handle,
                        ["name"] = p.Name,
                        ["price_in_cents"] = p.PriceInCents,
                        ["interval"] = 1,
                        ["interval_unit"] = "month"
                    }
                })
                : Enumerable.Empty<JsonObject>();
            return Json(HttpStatusCode.OK, new JsonArray(products.ToArray<JsonNode>()));
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            var reference = query["reference"]!;
            return CustomersByReference.TryGetValue(reference, out var id)
                ? Json(HttpStatusCode.OK, Customer(id, reference))
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            Interlocked.Increment(ref CreateCustomerPosts);
            var customer = JsonNode.Parse(body!)!["customer"]!;
            var reference = customer["reference"]!.GetValue<string>();
            var id = Interlocked.Increment(ref _nextId);
            if (!CustomersByReference.TryAdd(reference, id))
            {
                return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Reference must be unique") });
            }
            if (DropConnectionAfterCreatingCustomer)
            {
                DropConnectionAfterCreatingCustomer = false;
                throw new HttpRequestException("connection reset after customer was created");
            }
            return Json(HttpStatusCode.Created, Customer(id, reference));
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            var customerId = int.Parse(path.Split('/')[2]);
            lock (_gate)
            {
                var items = Subscriptions.Where(s => s["customer"]!["id"]!.GetValue<int>() == customerId)
                    .Select(s => (JsonNode)new JsonObject { ["subscription"] = s.DeepClone() }).ToArray();
                return Json(HttpStatusCode.OK, new JsonArray(items));
            }
        }

        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            var reference = query["reference"];
            lock (_gate)
            {
                var found = Subscriptions.FirstOrDefault(s => s["reference"]?.GetValue<string>() == reference);
                return found is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = found.DeepClone() });
            }
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            Interlocked.Increment(ref CreateSubscriptionPosts);
            if (CreateSubscriptionDelay > TimeSpan.Zero)
            {
                await Task.Delay(CreateSubscriptionDelay, cancellationToken);
            }
            if (DropConnectionBeforeCreatingSubscription)
            {
                DropConnectionBeforeCreatingSubscription = false;
                throw new HttpRequestException("connection refused");
            }
            if (RejectNextSubscriptionWith is { } errors)
            {
                RejectNextSubscriptionWith = null;
                return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray(errors.Select(e => (JsonNode)e!).ToArray()) });
            }

            var requested = JsonNode.Parse(body!)!["subscription"]!;
            var handle = requested["product_handle"]!.GetValue<string>();
            var product = Products.Single(p => p.Handle == handle);
            var subscription = new JsonObject
            {
                ["id"] = Interlocked.Increment(ref _nextId),
                ["state"] = "active",
                ["reference"] = requested["reference"]?.GetValue<string>(),
                ["product_price_in_cents"] = product.PriceInCents,
                ["currency"] = "USD",
                ["created_at"] = "2026-10-20T12:00:00Z",
                ["next_assessment_at"] = "2026-11-20T12:00:00Z",
                ["current_period_ends_at"] = "2026-11-20T12:00:00Z",
                ["product"] = new JsonObject { ["handle"] = product.Handle, ["name"] = product.Name },
                ["customer"] = new JsonObject { ["id"] = requested["customer_id"]!.GetValue<int>() },
                ["payment_collection_method"] = requested["payment_collection_method"]?.GetValue<string>()
            };
            lock (_gate)
            {
                Subscriptions.Add(subscription);
            }
            if (DropConnectionAfterCreatingSubscription)
            {
                DropConnectionAfterCreatingSubscription = false;
                throw new HttpRequestException("connection reset after subscription was created");
            }
            return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = subscription.DeepClone() });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static JsonObject Customer(int id, string reference) => new()
    {
        ["customer"] = new JsonObject { ["id"] = id, ["reference"] = reference, ["email"] = "shopper@example.com" }
    };

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) => new(status)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json")
    };
}
