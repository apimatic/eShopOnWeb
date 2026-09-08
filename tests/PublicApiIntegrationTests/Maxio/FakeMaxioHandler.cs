using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.Maxio;

public sealed class FakeMaxioCustomer
{
    public int Id { get; set; }
    public required string Reference { get; set; }
    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
}

public sealed class FakeMaxioSubscription
{
    public int Id { get; set; }
    public required string CustomerReference { get; set; }
    public required string ProductHandle { get; set; }
    public string State { get; set; } = "active";
}

/// <summary>
/// A stateful in-memory stand-in for the Maxio REST surface the SDK calls, so service behavior (customer
/// find-or-create, subscription idempotency, duplicate suppression) is exercised without the network.
/// </summary>
public sealed class FakeMaxioHandler : HttpMessageHandler
{
    private readonly object _sync = new();
    private int _nextCustomerId = 100;
    private int _nextSubscriptionId = 500;

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<FakeMaxioCustomer> Customers { get; } = new();

    public List<FakeMaxioSubscription> Subscriptions { get; } = new();

    public int SubscriptionPosts => Requests.Count(r =>
        r.Method == HttpMethod.Post && r.RequestUri != null && r.RequestUri.AbsolutePath.EndsWith("/subscriptions.json"));

    public int CustomerPosts => Requests.Count(r =>
        r.Method == HttpMethod.Post && r.RequestUri != null && r.RequestUri.AbsolutePath.EndsWith("/customers.json"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            Requests.Add(request);
        }

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var method = request.Method;
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

        HttpResponseMessage Respond(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        HttpResponseMessage Empty(HttpStatusCode status)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            };
        }

        if (path.EndsWith("/products.json", StringComparison.OrdinalIgnoreCase))
        {
            return Respond(JsonSerializer.Serialize(ProductFixtures.Envelopes));
        }

        if (path.EndsWith("/site.json", StringComparison.OrdinalIgnoreCase))
        {
            return Respond("""{"site":{"id":1,"currency":"USD","name":"eShop Sandbox","subdomain":"cp-exp-8"}}""");
        }

        if (path.Contains("/customers/lookup.json", StringComparison.OrdinalIgnoreCase))
        {
            var reference = request.RequestUri!.Query.Contains("reference=")
                ? Uri.UnescapeDataString(request.RequestUri.Query.Split('=')[1])
                : null;
            lock (_sync)
            {
                var customer = Customers.FirstOrDefault(c => string.Equals(c.Reference, reference, StringComparison.OrdinalIgnoreCase));
                return customer is null ? Empty(HttpStatusCode.NotFound) : Respond(JsonSerializer.Serialize(CustomerEnvelope(customer)));
            }
        }

        if (method == HttpMethod.Post && path.EndsWith("/customers.json", StringComparison.OrdinalIgnoreCase))
        {
            lock (_sync)
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("customer", out var customerElement))
                {
                    var reference = Read(customerElement, "reference");
                    var existing = Customers.FirstOrDefault(c => string.Equals(c.Reference, reference, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null)
                    {
                        return Respond("""{"errors":{"per_page":["Customer already exists"]}}""", HttpStatusCode.UnprocessableEntity);
                    }

                    var customer = new FakeMaxioCustomer
                    {
                        Id = _nextCustomerId++,
                        Reference = reference ?? string.Empty,
                        Email = Read(customerElement, "email") ?? string.Empty,
                        FirstName = Read(customerElement, "first_name") ?? string.Empty,
                        LastName = Read(customerElement, "last_name") ?? string.Empty
                    };
                    Customers.Add(customer);
                    return Respond(JsonSerializer.Serialize(CustomerEnvelope(customer)));
                }

                return Empty(HttpStatusCode.UnprocessableEntity);
            }
        }

        if (method == HttpMethod.Post && path.EndsWith("/subscriptions.json", StringComparison.OrdinalIgnoreCase))
        {
            lock (_sync)
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("subscription", out var subscriptionElement))
                {
                    return Empty(HttpStatusCode.UnprocessableEntity);
                }

                var productHandle = Read(subscriptionElement, "product_handle");
                var customerReference = Read(subscriptionElement, "customer_reference");
                var customer = Customers.FirstOrDefault(c => string.Equals(c.Reference, customerReference, StringComparison.OrdinalIgnoreCase));
                var product = ProductFixtures.ByHandle(productHandle);
                if (customer is null || product is null)
                {
                    return Respond("""{"errors":["The selected product or customer is not valid"]}""", HttpStatusCode.UnprocessableEntity);
                }

                var subscription = new FakeMaxioSubscription
                {
                    Id = _nextSubscriptionId++,
                    CustomerReference = customer.Reference,
                    ProductHandle = productHandle ?? string.Empty,
                    State = "active"
                };
                Subscriptions.Add(subscription);
                return Respond(JsonSerializer.Serialize(SubscriptionEnvelope(subscription)));
            }
        }

        if (method == HttpMethod.Get && path.EndsWith("/subscriptions.json", StringComparison.OrdinalIgnoreCase))
        {
            lock (_sync)
            {
                var match = Regex.Match(path, "/customers/(\\d+)/subscriptions.json");
                if (!match.Success)
                {
                    return Empty(HttpStatusCode.NotFound);
                }

                var customerId = int.Parse(match.Groups[1].Value);
                var customer = Customers.FirstOrDefault(c => c.Id == customerId);
                if (customer is null)
                {
                    return Empty(HttpStatusCode.NotFound);
                }

                var subscriptions = Subscriptions
                    .Where(s => string.Equals(s.CustomerReference, customer.Reference, StringComparison.OrdinalIgnoreCase))
                    .Select(SubscriptionEnvelope)
                    .ToArray();
                return Respond(JsonSerializer.Serialize(subscriptions));
            }
        }

        if (method == HttpMethod.Get && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && path.Contains("/subscriptions/"))
        {
            lock (_sync)
            {
                var match = Regex.Match(path, "/subscriptions/(\\d+).json");
                if (!match.Success)
                {
                    return Empty(HttpStatusCode.NotFound);
                }

                var subscriptionId = int.Parse(match.Groups[1].Value);
                var subscription = Subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
                return subscription is null
                    ? Empty(HttpStatusCode.NotFound)
                    : Respond(JsonSerializer.Serialize(SubscriptionEnvelope(subscription)));
            }
        }

        return Empty(HttpStatusCode.NotFound);
    }

    private static Dictionary<string, object?> CustomerEnvelope(FakeMaxioCustomer customer)
    {
        return new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?>
            {
                ["id"] = customer.Id,
                ["reference"] = customer.Reference,
                ["email"] = customer.Email,
                ["first_name"] = customer.FirstName,
                ["last_name"] = customer.LastName
            }
        };
    }

    private Dictionary<string, object?> SubscriptionEnvelope(FakeMaxioSubscription subscription)
    {
        var product = ProductFixtures.ByHandle(subscription.ProductHandle);
        return new Dictionary<string, object?>
        {
            ["subscription"] = new Dictionary<string, object?>
            {
                ["id"] = subscription.Id,
                ["state"] = subscription.State,
                ["product"] = product is null ? null : ProductFixtures.ProductObject(product),
                ["product_price_in_cents"] = product?.PriceInCents ?? 0L,
                ["currency"] = "USD",
                ["current_period_ends_at"] = "2026-10-09T12:00:00Z",
                ["next_assessment_at"] = "2026-10-09T12:00:00Z"
            }
        };
    }

    private static string? Read(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

public static class ProductFixtures
{
    public sealed record Product(int Id, string Handle, string Name, long PriceInCents, int Interval, string IntervalUnit);

    public static readonly Product Pro = new(1, "eshop-pro", "Pro Plan", 29900, 1, "month");
    public static readonly Product Basic = new(2, "basic-plan", "Basic Plan", 2900, 1, "month");

    public static List<Product> All { get; } = new() { Pro, Basic };

    public static List<Dictionary<string, object?>> Envelopes => All
        .Select(p => new Dictionary<string, object?> { ["product"] = ProductObject(p) })
        .ToList();

    public static Product? ByHandle(string? handle)
    {
        return All.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    public static Dictionary<string, object?> ProductObject(Product product)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = product.Id,
            ["handle"] = product.Handle,
            ["name"] = product.Name,
            ["price_in_cents"] = product.PriceInCents,
            ["interval"] = product.Interval,
            ["interval_unit"] = product.IntervalUnit
        };
    }
}
