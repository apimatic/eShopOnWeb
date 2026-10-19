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
/// In-process stand-in for the Maxio Advanced Billing API, plugged in as the primary handler of the SDK's
/// HttpClient. Keeps customers and subscriptions in memory and can inject failures.
/// </summary>
public class FakeMaxio : HttpMessageHandler
{
    public const string FamilyHandle = "test-family";

    private readonly object _lock = new();
    private int _nextId = 1000;

    public List<JsonObject> Products { get; } = new()
    {
        Product(7001, "eshop-pro", "Pro Plan", 29900),
        Product(7002, "basic-plan", "Basic Plan", 2900),
        Product(7003, "retired-plan", "Retired Plan", 1000, archived: true)
    };

    public ConcurrentDictionary<string, JsonObject> CustomersByReference { get; } = new();
    public ConcurrentDictionary<string, JsonObject> SubscriptionsByReference { get; } = new();
    public ConcurrentQueue<(HttpMethod Method, string Path, string? Body)> Requests { get; } = new();

    /// <summary>Commit the next customer create, then drop the connection before answering.</summary>
    public bool DropAfterCustomerCreate { get; set; }

    /// <summary>Commit the next subscription create, then drop the connection before answering.</summary>
    public bool DropAfterSubscriptionCreate { get; set; }

    /// <summary>Reject subscription creates with this 422 error message.</summary>
    public string? RejectSubscriptionWith { get; set; }

    /// <summary>When set, subscription creates wait for this gate before committing.</summary>
    public TaskCompletionSource? SubscriptionCreateGate { get; set; }

    public int Count(HttpMethod method, string path) => Requests.Count(r => r.Method == method && r.Path == path);

    public static JsonObject Product(int id, string handle, string name, long priceInCents, bool archived = false) => new()
    {
        ["id"] = id,
        ["handle"] = handle,
        ["name"] = name,
        ["price_in_cents"] = priceInCents,
        ["interval"] = 1,
        ["interval_unit"] = "month",
        ["require_credit_card"] = false,
        ["archived_at"] = archived ? "2026-01-01T00:00:00Z" : null
    };

    public JsonObject SeedSubscription(string reference, string productHandle, int customerId)
    {
        var product = Products.First(p => (string?)p["handle"] == productHandle);
        var sub = NewSubscription(reference, product, customerId);
        SubscriptionsByReference[reference] = sub;
        return sub;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Read the body now: the SDK disposes request content after each attempt.
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        Requests.Enqueue((request.Method, path, body));
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);

        if (request.Method == HttpMethod.Get && path == $"/product_families/handle:{FamilyHandle}/products.json")
        {
            return Json(HttpStatusCode.OK, new JsonArray(Products.Select(p => (JsonNode)new JsonObject { ["product"] = p.DeepClone() }).ToArray()));
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            return CustomersByReference.TryGetValue(query["reference"] ?? "", out var c)
                ? Json(HttpStatusCode.OK, new JsonObject { ["customer"] = c.DeepClone() })
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            var input = JsonNode.Parse(body!)!["customer"]!.AsObject();
            var reference = (string)input["reference"]!;
            JsonObject customer;
            lock (_lock)
            {
                customer = new JsonObject
                {
                    ["id"] = _nextId++,
                    ["first_name"] = (string?)input["first_name"],
                    ["last_name"] = (string?)input["last_name"],
                    ["email"] = (string?)input["email"],
                    ["reference"] = reference
                };
                CustomersByReference[reference] = customer;
            }
            if (DropAfterCustomerCreate)
            {
                DropAfterCustomerCreate = false;
                throw new HttpRequestException("connection reset (injected)");
            }
            return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = customer.DeepClone() });
        }

        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            return SubscriptionsByReference.TryGetValue(query["reference"] ?? "", out var s)
                ? Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = s.DeepClone() })
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            if (SubscriptionCreateGate != null)
            {
                await SubscriptionCreateGate.Task.WaitAsync(cancellationToken);
            }
            if (RejectSubscriptionWith != null)
            {
                return Json((HttpStatusCode)422, new JsonObject { ["errors"] = new JsonArray(RejectSubscriptionWith) });
            }
            var input = JsonNode.Parse(body!)!["subscription"]!.AsObject();
            var product = Products.First(p => (string?)p["handle"] == (string?)input["product_handle"]);
            var sub = NewSubscription((string)input["reference"]!, product, (int)input["customer_id"]!);
            SubscriptionsByReference[(string)input["reference"]!] = sub;
            if (DropAfterSubscriptionCreate)
            {
                DropAfterSubscriptionCreate = false;
                throw new HttpRequestException("connection reset (injected)");
            }
            return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = sub.DeepClone() });
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            var customerId = int.Parse(path.Split('/')[2]);
            var subs = SubscriptionsByReference.Values
                .Where(s => (int?)s["customer"]?["id"] == customerId)
                .Select(s => (JsonNode)new JsonObject { ["subscription"] = s.DeepClone() })
                .ToArray();
            return Json(HttpStatusCode.OK, new JsonArray(subs));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("unexpected route " + path) };
    }

    private JsonObject NewSubscription(string reference, JsonObject product, int customerId)
    {
        int id;
        lock (_lock) { id = _nextId++; }
        var now = DateTimeOffset.UtcNow;
        return new JsonObject
        {
            ["id"] = id,
            ["state"] = "active",
            ["reference"] = reference,
            ["product_price_in_cents"] = (long)product["price_in_cents"]!,
            ["currency"] = "USD",
            ["next_assessment_at"] = now.AddMonths(1).ToString("O"),
            ["current_period_ends_at"] = now.AddMonths(1).ToString("O"),
            ["activated_at"] = now.ToString("O"),
            ["created_at"] = now.ToString("O"),
            ["customer"] = new JsonObject { ["id"] = customerId },
            ["product"] = product.DeepClone()
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) => new(status)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json")
    };
}
