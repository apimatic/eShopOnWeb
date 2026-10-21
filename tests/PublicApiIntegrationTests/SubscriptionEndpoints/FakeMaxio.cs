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
using Microsoft.AspNetCore.WebUtilities;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// An in-process stand-in for the Maxio Advanced Billing API, installed as the primary handler of the
/// SDK's HttpClient. It keeps customers and subscriptions in memory, speaks the JSON wire shapes the SDK
/// models declare, and can inject the failures the integration must survive.
/// </summary>
public sealed class FakeMaxio : HttpMessageHandler
{
    private readonly object _gate = new();
    private int _nextId = 1000;

    public string FamilyHandle { get; set; } = "test-family";
    public List<FakeProduct> Products { get; } = new()
    {
        new FakeProduct("pro-plan", "Pro Plan", 29900),
        new FakeProduct("basic-plan", "Basic Plan", 2900),
        new FakeProduct("old-plan", "Retired Plan", 1000, Archived: true)
    };

    public List<FakeCustomer> Customers { get; } = new();
    public List<FakeSubscription> Subscriptions { get; } = new();
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>Every request is answered with this status instead (e.g. 401 for bad credentials).</summary>
    public HttpStatusCode? ForceStatus { get; set; }

    /// <summary>Delay applied inside create calls, to make concurrent requests overlap.</summary>
    public TimeSpan CreateDelay { get; set; } = TimeSpan.Zero;

    /// <summary>The subscription is created, then the connection drops before the response arrives.</summary>
    public bool DropResponseAfterCreatingSubscription { get; set; }

    /// <summary>The customer is created, then the connection drops before the response arrives.</summary>
    public bool DropResponseAfterCreatingCustomer { get; set; }

    /// <summary>The connection drops before the create reaches Maxio (nothing is created).</summary>
    public bool DropConnectionBeforeCreatingSubscription { get; set; }

    /// <summary>When set, subscription creates are refused with 422 and these errors.</summary>
    public string[]? RejectSubscriptionErrors { get; set; }

    public int CountRequests(HttpMethod method, string path) =>
        Requests.Count(r => r.Method == method && r.Path == path);

    public FakeCustomer AddCustomer(string reference, string email)
    {
        lock (_gate)
        {
            var customer = new FakeCustomer(++_nextId, reference, email, "Existing", "Customer");
            Customers.Add(customer);
            return customer;
        }
    }

    public FakeSubscription AddSubscription(FakeCustomer customer, string productHandle, string state = "active", string? reference = null)
    {
        lock (_gate)
        {
            var product = Products.Single(p => p.Handle == productHandle);
            var subscription = new FakeSubscription(++_nextId, customer, product, state, reference, DateTimeOffset.UtcNow);
            Subscriptions.Add(subscription);
            return subscription;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue(new RecordedRequest(request.Method, uri.Host, path, uri.Query, body, request.Headers.Authorization?.ToString()));

        if (ForceStatus is not null)
        {
            return Json(ForceStatus.Value, new JsonObject { ["errors"] = new JsonArray("forced") });
        }

        var query = QueryHelpers.ParseQuery(uri.Query);

        if (request.Method == HttpMethod.Get && path == $"/product_families/handle:{FamilyHandle}/products.json")
        {
            var page = int.Parse(query["page"].ToString());
            var perPage = int.Parse(query["per_page"].ToString());
            var items = Products.Skip((page - 1) * perPage).Take(perPage).Select(p => new JsonObject { ["product"] = ProductJson(p) });
            return Json(HttpStatusCode.OK, new JsonArray(items.ToArray<JsonNode?>()));
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/product_families/", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.NotFound, JsonValue.Create("Not Found"));
        }

        if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
        {
            var customer = Find(() => Customers.FirstOrDefault(c => c.Reference == query["reference"].ToString()));
            return customer is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.OK, new JsonObject { ["customer"] = CustomerJson(customer) });
        }

        if (request.Method == HttpMethod.Post && path == "/customers.json")
        {
            await Task.Delay(CreateDelay, cancellationToken);
            var input = JsonNode.Parse(body!)!["customer"]!;
            var reference = input["reference"]?.GetValue<string>();
            lock (_gate)
            {
                // Maxio keeps customer references unique per site.
                if (reference is not null && Customers.Any(c => c.Reference == reference))
                {
                    return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Reference must be unique.") });
                }
                var customer = new FakeCustomer(++_nextId, reference, input["email"]!.GetValue<string>(),
                    input["first_name"]!.GetValue<string>(), input["last_name"]!.GetValue<string>());
                Customers.Add(customer);
                if (DropResponseAfterCreatingCustomer)
                {
                    throw new HttpRequestException("connection reset (simulated, after the customer was created)");
                }
                return Json(HttpStatusCode.Created, new JsonObject { ["customer"] = CustomerJson(customer) });
            }
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/customers/", StringComparison.Ordinal) && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
        {
            var customerId = int.Parse(path.Split('/')[2]);
            var items = Find(() => Subscriptions.Where(s => s.Customer.Id == customerId).ToList())
                .Select(s => new JsonObject { ["subscription"] = SubscriptionJson(s) });
            return Json(HttpStatusCode.OK, new JsonArray(items.ToArray<JsonNode?>()));
        }

        if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
        {
            await Task.Delay(CreateDelay, cancellationToken);
            if (DropConnectionBeforeCreatingSubscription)
            {
                throw new HttpRequestException("connection reset (simulated, before send)");
            }
            if (RejectSubscriptionErrors is not null)
            {
                return Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray(RejectSubscriptionErrors.Select(e => (JsonNode?)e).ToArray()) });
            }

            var input = JsonNode.Parse(body!)!["subscription"]!;
            FakeSubscription subscription;
            lock (_gate)
            {
                var customer = Customers.Single(c => c.Id == input["customer_id"]!.GetValue<int>());
                var product = Products.Single(p => p.Handle == input["product_handle"]!.GetValue<string>());
                subscription = new FakeSubscription(++_nextId, customer, product, "active", input["reference"]?.GetValue<string>(), DateTimeOffset.UtcNow);
                Subscriptions.Add(subscription);
            }
            if (DropResponseAfterCreatingSubscription)
            {
                throw new HttpRequestException("connection reset (simulated, after the subscription was created)");
            }
            return Json(HttpStatusCode.Created, new JsonObject { ["subscription"] = SubscriptionJson(subscription) });
        }

        if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
        {
            var subscription = Find(() => Subscriptions.FirstOrDefault(s => s.Reference != null && s.Reference == query["reference"].ToString()));
            return subscription is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.OK, new JsonObject { ["subscription"] = SubscriptionJson(subscription) });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"FakeMaxio has no route for {request.Method} {path}") };
    }

    private T Find<T>(Func<T> query)
    {
        lock (_gate)
        {
            return query();
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode? node) =>
        new(status) { Content = new StringContent(node?.ToJsonString() ?? "null", Encoding.UTF8, "application/json") };

    private static JsonObject ProductJson(FakeProduct p) => new()
    {
        ["id"] = Math.Abs(p.Handle.GetHashCode() % 100000),
        ["name"] = p.Name,
        ["handle"] = p.Handle,
        ["description"] = $"{p.Name} description",
        ["price_in_cents"] = p.PriceInCents,
        ["interval"] = 1,
        ["interval_unit"] = "month",
        ["archived_at"] = p.Archived ? DateTimeOffset.UtcNow.AddDays(-1).ToString("O") : null
    };

    private static JsonObject CustomerJson(FakeCustomer c) => new()
    {
        ["id"] = c.Id,
        ["reference"] = c.Reference,
        ["email"] = c.Email,
        ["first_name"] = c.FirstName,
        ["last_name"] = c.LastName
    };

    private static JsonObject SubscriptionJson(FakeSubscription s) => new()
    {
        ["id"] = s.Id,
        ["state"] = s.State,
        ["reference"] = s.Reference,
        ["product_price_in_cents"] = s.Product.PriceInCents,
        ["currency"] = "USD",
        ["created_at"] = s.CreatedAt.ToString("O"),
        ["activated_at"] = s.CreatedAt.ToString("O"),
        ["current_period_ends_at"] = s.CreatedAt.AddMonths(1).ToString("O"),
        ["next_assessment_at"] = s.CreatedAt.AddMonths(1).ToString("O"),
        ["product"] = ProductJson(s.Product),
        ["customer"] = CustomerJson(s.Customer)
    };
}

public sealed record FakeProduct(string Handle, string Name, long PriceInCents, bool Archived = false);

public sealed record FakeCustomer(int Id, string? Reference, string Email, string FirstName, string LastName);

public sealed record FakeSubscription(int Id, FakeCustomer Customer, FakeProduct Product, string State, string? Reference, DateTimeOffset CreatedAt);

public sealed record RecordedRequest(HttpMethod Method, string Host, string Path, string Query, string? Body, string? Authorization);
