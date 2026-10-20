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
/// A stateful stand-in for Maxio Advanced Billing behind the SDK's HttpClient seam. It serves the operations the
/// subscription flow uses and lets a test inject the failures that matter: a connection lost after the provider
/// acted, a validation rejection, a slow write.
/// </summary>
public class FakeMaxio : HttpMessageHandler
{
    public const string BaseUrl = "https://maxio.test";
    public const string FamilyHandle = "eshop-subscribe";

    private readonly object _lock = new();
    private int _nextId = 1000;

    public ConcurrentDictionary<string, JsonObject> CustomersByReference { get; } = new();
    public ConcurrentDictionary<string, JsonObject> SubscriptionsByReference { get; } = new();
    public ConcurrentQueue<string> Requests { get; } = new();

    public List<(string Handle, string Name, long PriceInCents)> Products { get; } = new()
    {
        ("basic-plan", "Basic Plan", 2900),
        ("eshop-pro", "Pro Plan", 29900)
    };

    /// <summary>When set, every product page is full (PerPage distinct products), so paging never ends on its own.</summary>
    public bool EndlessProductPages { get; set; }

    /// <summary>When set, every product page returns the same full page, so paging makes no progress.</summary>
    public bool RepeatingProductPages { get; set; }

    public TimeSpan CreateSubscriptionDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Applied to the next create: "drop-after-apply", "drop-before-apply", "garbage-after-apply", "reject".</summary>
    public string? NextSubscriptionCreateFailure { get; set; }

    public string? NextCustomerCreateFailure { get; set; }

    public int CustomerCreates => Requests.Count(r => r == "POST /customers.json");
    public int SubscriptionCreates => Requests.Count(r => r == "POST /subscriptions.json");

    public string? LastSubscriptionCreateBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue($"{request.Method} {path}");

        if (request.Method == HttpMethod.Get && path == $"/product_families/handle:{FamilyHandle}/products.json")
        {
            return Json(HttpStatusCode.OK, ProductPage(int.Parse(query["page"] ?? "1"), int.Parse(query["per_page"] ?? "20")));
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            return CustomersByReference.TryGetValue(query["reference"] ?? "", out var customer)
                ? Json(HttpStatusCode.OK, new JsonObject { ["customer"] = customer.DeepClone() })
                : Status(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            return CreateCustomer(body!);
        }

        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            return SubscriptionsByReference.TryGetValue(query["reference"] ?? "", out var subscription)
                ? Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = subscription.DeepClone() })
                : Status(HttpStatusCode.NotFound);
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            LastSubscriptionCreateBody = body;
            if (CreateSubscriptionDelay > TimeSpan.Zero)
            {
                await Task.Delay(CreateSubscriptionDelay, cancellationToken);
            }
            return CreateSubscription(body!);
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/subscriptions/") && path.EndsWith(".json"))
        {
            var id = int.Parse(path["/subscriptions/".Length..^".json".Length]);
            var subscription = SubscriptionsByReference.Values.FirstOrDefault(s => (int)s["id"]! == id);
            return subscription is null
                ? Status(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = subscription.DeepClone() });
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/") && path.EndsWith("/subscriptions.json"))
        {
            var customerId = int.Parse(path.Split('/')[2]);
            var list = new JsonArray(SubscriptionsByReference.Values
                .Where(s => (int)s["customer"]!["id"]! == customerId)
                .Select(s => (JsonNode)new JsonObject { ["subscription"] = s.DeepClone() })
                .ToArray());
            return Json(HttpStatusCode.OK, list);
        }

        return Status(HttpStatusCode.NotFound);
    }

    private HttpResponseMessage CreateCustomer(string body)
    {
        var failure = NextCustomerCreateFailure;
        NextCustomerCreateFailure = null;

        if (failure == "reject")
        {
            return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Email address: is invalid.") });
        }
        if (failure == "drop-before-apply")
        {
            throw new HttpRequestException("connection reset");
        }

        var input = JsonNode.Parse(body)!["customer"]!;
        var reference = (string)input["reference"]!;
        JsonObject customer;
        lock (_lock)
        {
            if (CustomersByReference.ContainsKey(reference))
            {
                return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Reference must be unique.") });
            }
            customer = new JsonObject
            {
                ["id"] = Interlocked.Increment(ref _nextId),
                ["reference"] = reference,
                ["email"] = (string)input["email"]!,
                ["first_name"] = (string)input["first_name"]!,
                ["last_name"] = (string)input["last_name"]!
            };
            CustomersByReference[reference] = customer;
        }

        if (failure == "drop-after-apply")
        {
            throw new HttpRequestException("connection reset after the customer was created");
        }
        return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = customer.DeepClone() });
    }

    private HttpResponseMessage CreateSubscription(string body)
    {
        var failure = NextSubscriptionCreateFailure;
        NextSubscriptionCreateFailure = null;

        if (failure == "reject")
        {
            return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("No payment method was on file for the $299.00 balance") });
        }
        if (failure == "drop-before-apply")
        {
            throw new HttpRequestException("connection reset");
        }

        var input = JsonNode.Parse(body)!["subscription"]!;
        var reference = (string)input["reference"]!;
        var handle = (string)input["product_handle"]!;
        var customerId = (int)input["customer_id"]!;
        var product = Products.First(p => p.Handle == handle);

        var now = DateTimeOffset.UtcNow;
        var subscription = new JsonObject
        {
            ["id"] = Interlocked.Increment(ref _nextId),
            ["state"] = "active",
            ["reference"] = reference,
            ["currency"] = "USD",
            ["product_price_in_cents"] = product.PriceInCents,
            ["activated_at"] = now.ToString("O"),
            ["current_period_ends_at"] = now.AddMonths(1).ToString("O"),
            ["next_assessment_at"] = now.AddMonths(1).ToString("O"),
            ["payment_collection_method"] = (string?)input["payment_collection_method"],
            ["customer"] = new JsonObject { ["id"] = customerId },
            ["product"] = ProductJson(product.Handle, product.Name, product.PriceInCents, id: 1)
        };
        SubscriptionsByReference[reference] = subscription;

        if (failure == "drop-after-apply")
        {
            throw new HttpRequestException("connection reset after the subscription was created");
        }
        if (failure == "garbage-after-apply")
        {
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"subscription\": \"not-an-object\"}", Encoding.UTF8, "application/json")
            };
        }
        return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = subscription.DeepClone() });
    }

    private JsonArray ProductPage(int page, int perPage)
    {
        if (EndlessProductPages || RepeatingProductPages)
        {
            var firstId = RepeatingProductPages ? 1 : (page - 1) * perPage + 1;
            return new JsonArray(Enumerable.Range(firstId, perPage)
                .Select(id => (JsonNode)new JsonObject { ["product"] = ProductJson($"generated-{id}", $"Generated {id}", 100, id) })
                .ToArray());
        }

        var items = Products.Skip((page - 1) * perPage).Take(perPage)
            .Select((p, i) => (JsonNode)new JsonObject { ["product"] = ProductJson(p.Handle, p.Name, p.PriceInCents, i + 1) });
        return new JsonArray(items.ToArray());
    }

    private static JsonObject ProductJson(string handle, string name, long priceInCents, int id) => new()
    {
        ["id"] = id,
        ["handle"] = handle,
        ["name"] = name,
        ["price_in_cents"] = priceInCents,
        ["interval"] = 1,
        ["interval_unit"] = "month",
        ["product_family"] = new JsonObject { ["handle"] = FamilyHandle }
    };

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) => new(status)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status)
    {
        Content = new StringContent("", Encoding.UTF8, "application/json")
    };
}
