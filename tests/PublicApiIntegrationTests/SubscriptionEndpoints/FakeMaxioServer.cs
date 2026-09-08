using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

public sealed class CustomerRecord
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class SubscriptionRecord
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// An in-memory stand-in for the Maxio Advanced Billing HTTP API, used to exercise the SDK
/// and the subscription service without a network. Matches the real wire routes and bodies.
/// </summary>
public sealed class FakeMaxioServer : IDisposable
{
    public const int FamilyId = 3023074;
    public const string FamilyHandle = "eshop-subscribe";

    public static readonly (long Id, string Handle, string Name, long PriceInCents)[] Catalog =
    {
        (7126957, "eshop-pro", "Pro Plan", 29900),
        (7126958, "basic-plan", "Basic Plan", 2900)
    };

    private readonly object _sync = new();
    private readonly List<CustomerRecord> _customers = new();
    private readonly List<SubscriptionRecord> _subscriptions = new();
    private readonly StubHandler _handler = new();
    private int _nextCustomerId = 4000001;
    private int _nextSubscriptionId = 9000001;

    public FakeMaxioServer()
    {
        _handler.Responder = RespondAsync;
    }

    /// <summary>When true, product list payloads omit pricing so the service exercises the default-price-point fallback.</summary>
    public bool OmitProductPricing { get; set; }

    /// <summary>When true, a customer create for a new reference inserts the customer then answers 422 (a lost double-click race).</summary>
    public bool InsertCustomerThenRejectCreate { get; set; }

    /// <summary>When true, a subscription create inserts the subscription then answers 422 (a lost double-click race on subscribe).</summary>
    public bool InsertSubscriptionThenRejectCreate { get; set; }

    public HttpMessageHandler Handler => _handler;

    public IReadOnlyList<string> Transcript => _handler.Transcript;

    public IReadOnlyList<CustomerRecord> Customers
    {
        get { lock (_sync) { return _customers.ToList(); } }
    }

    public IReadOnlyList<SubscriptionRecord> Subscriptions
    {
        get { lock (_sync) { return _subscriptions.ToList(); } }
    }

    public IReadOnlyList<HttpRequestMessage> Requests => _handler.Requests;

    public CustomerRecord EnsureCustomer(string reference, string email = "shopper@example.com", string firstName = "Test", string lastName = "Shopper")
    {
        lock (_sync)
        {
            var existing = _customers.FirstOrDefault(c => c.Reference == reference);
            if (existing is not null)
            {
                return existing;
            }

            var created = new CustomerRecord
            {
                Id = _nextCustomerId++,
                Reference = reference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            };
            _customers.Add(created);
            return created;
        }
    }

    public SubscriptionRecord AddSubscription(string reference, string productHandle, string state = "active")
    {
        lock (_sync)
        {
            var customer = _customers.FirstOrDefault(c => c.Reference == reference)
                ?? EnsureCustomer(reference);
            var created = new SubscriptionRecord
            {
                Id = _nextSubscriptionId++,
                CustomerId = customer.Id,
                ProductHandle = productHandle,
                State = state
            };
            _subscriptions.Add(created);
            return created;
        }
    }

    public MaxioSubscriptionService CreateService(MaxioOptions? options = null)
    {
        var client = new MaxioAdvancedBillingClient(
            new HttpClient(Handler, disposeHandler: false),
            new MaxioAdvancedBillingClientOptions());

        options ??= new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "sandbox-site",
            ProductFamilyHandle = FamilyHandle
        };

        return new MaxioSubscriptionService(client, Options.Create(options), NullLogger<MaxioSubscriptionService>.Instance);
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        var method = request.Method.Method.ToUpperInvariant();

        if (method == "GET" && path == "/product_families.json")
        {
            return Json(HttpStatusCode.OK, FamiliesPayload());
        }

        if (method == "GET" && path.EndsWith("/products.json", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, ProductsPayload());
        }

        if (method == "GET" && path.StartsWith("/products/handle/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal))
        {
            var handle = path.Substring("/products/handle/".Length, path.Length - "/products/handle/".Length - ".json".Length);
            var product = Catalog.FirstOrDefault(p => p.Handle == handle);
            return product.Handle is null
                ? Json(HttpStatusCode.NotFound, "{\"errors\": \"Not Found\"}")
                : Json(HttpStatusCode.OK, new Dictionary<string, object?> { ["product"] = ProductPayload(product, includePricing: true) });
        }

        if (method == "GET" && path.Contains("/price_points.json", StringComparison.Ordinal))
        {
            var productIdSegment = path.Split('/')[2];
            var product = Catalog.FirstOrDefault(p => p.Id.ToString() == productIdSegment);
            return Json(HttpStatusCode.OK, PricePointsPayload(product));
        }

        if (method == "GET" && path == "/customers/lookup.json")
        {
            var reference = Microsoft.AspNetCore.WebUtilities.QueryHelpers
                .ParseQuery(request.RequestUri.Query)["reference"].ToString();
            lock (_sync)
            {
                var customer = _customers.FirstOrDefault(c => c.Reference == reference);
                return customer is null
                    ? Json(HttpStatusCode.NotFound, "{\"errors\": \"Not Found\"}")
                    : Json(HttpStatusCode.OK, CustomerEnvelope(customer));
            }
        }

        if (method == "POST" && path == "/customers.json")
        {
            return await CreateCustomerAsync(request);
        }

        if (method == "GET" && path.EndsWith("/subscriptions.json", StringComparison.Ordinal))
        {
            var customerIdSegment = path.Split('/')[2];
            lock (_sync)
            {
                var subs = _subscriptions.Where(s => s.CustomerId.ToString() == customerIdSegment).ToList();
                return Json(HttpStatusCode.OK, subs.Select(SubscriptionEnvelope).ToList());
            }
        }

        if (method == "POST" && path == "/subscriptions.json")
        {
            return await CreateSubscriptionAsync(request);
        }

        return Json(HttpStatusCode.NotFound, "{\"errors\": \"Not Found\"}");
    }

    private Task<HttpResponseMessage> CreateCustomerAsync(HttpRequestMessage request)
    {
        using var doc = JsonDocument.Parse(ReadBody(request));
        var customerNode = doc.RootElement.GetProperty("customer");
        var reference = customerNode.GetProperty("reference").GetString() ?? string.Empty;
        var firstName = customerNode.TryGetProperty("first_name", out var fn) ? fn.GetString() : null;
        var lastName = customerNode.TryGetProperty("last_name", out var ln) ? ln.GetString() : null;
        var email = customerNode.TryGetProperty("email", out var em) ? em.GetString() : null;

        lock (_sync)
        {
            var existing = _customers.FirstOrDefault(c => c.Reference == reference);
            if (existing is not null)
            {
                return Task.FromResult(Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":{\"reference\":[\"has already been taken\"]}}"));
            }

            if (InsertCustomerThenRejectCreate)
            {
                _customers.Add(new CustomerRecord
                {
                    Id = _nextCustomerId++,
                    Reference = reference,
                    FirstName = firstName ?? string.Empty,
                    LastName = lastName ?? string.Empty,
                    Email = email ?? string.Empty
                });
                return Task.FromResult(Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":{\"reference\":[\"has already been taken\"]}}"));
            }

            var created = new CustomerRecord
            {
                Id = _nextCustomerId++,
                Reference = reference,
                FirstName = firstName ?? string.Empty,
                LastName = lastName ?? string.Empty,
                Email = email ?? string.Empty
            };
            _customers.Add(created);
            return Task.FromResult(Json(HttpStatusCode.Created, CustomerEnvelope(created)));
        }
    }

    private Task<HttpResponseMessage> CreateSubscriptionAsync(HttpRequestMessage request)
    {
        using var doc = JsonDocument.Parse(ReadBody(request));
        var subscriptionNode = doc.RootElement.GetProperty("subscription");
        var productHandle = subscriptionNode.TryGetProperty("product_handle", out var ph) ? ph.GetString() : null;
        var customerReference = subscriptionNode.TryGetProperty("customer_reference", out var cr) ? cr.GetString() : null;

        lock (_sync)
        {
            var customer = _customers.FirstOrDefault(c => c.Reference == customerReference);
            if (customer is null)
            {
                return Task.FromResult(Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"customer_reference not found\"]}"));
            }

            var product = Catalog.FirstOrDefault(p => p.Handle == productHandle);
            if (product.Handle is null)
            {
                return Task.FromResult(Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"product_handle not found\"]}"));
            }

            var existingActive = _subscriptions.FirstOrDefault(s =>
                s.CustomerId == customer.Id
                && s.ProductHandle == productHandle
                && !IsEnded(s.State));
            if (existingActive is not null)
            {
                return Task.FromResult(Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"The customer already has an active subscription to this product.\"]}"));
            }

            if (InsertSubscriptionThenRejectCreate)
            {
                _subscriptions.Add(new SubscriptionRecord
                {
                    Id = _nextSubscriptionId++,
                    CustomerId = customer.Id,
                    ProductHandle = productHandle!,
                    State = "active"
                });
                return Task.FromResult(Json(HttpStatusCode.UnprocessableEntity, "{\"errors\":[\"The customer already has an active subscription to this product.\"]}"));
            }

            var created = new SubscriptionRecord
            {
                Id = _nextSubscriptionId++,
                CustomerId = customer.Id,
                ProductHandle = productHandle!,
                State = "active"
            };
            _subscriptions.Add(created);
            return Task.FromResult(Json(HttpStatusCode.Created, SubscriptionEnvelope(created)));
        }
    }

    private static bool IsEnded(string state) =>
        state is "canceled" or "expired" or "trial_ended" or "failed_to_create";

    private static object FamiliesPayload()
    {
        return new[]
        {
            new Dictionary<string, object?>
            {
                ["product_family"] = new Dictionary<string, object?>
                {
                    ["id"] = FamilyId,
                    ["name"] = "eShop Subscription Plans",
                    ["handle"] = FamilyHandle,
                    ["description"] = "Subscription plans for eShopOnWeb"
                }
            }
        };
    }

    private object ProductsPayload()
    {
        return Catalog.Select(p => new Dictionary<string, object?>
        {
            ["product"] = ProductPayload(p, includePricing: !OmitProductPricing)
        }).ToList();
    }

    private static object PricePointsPayload((long Id, string Handle, string Name, long PriceInCents) product)
    {
        return new Dictionary<string, object?>
        {
            ["price_points"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = product.Id * 10,
                    ["name"] = "Default",
                    ["handle"] = product.Handle,
                    ["price_in_cents"] = product.PriceInCents,
                    ["interval"] = 1,
                    ["interval_unit"] = "month",
                    ["type"] = "default",
                    ["archived_at"] = null
                }
            }
        };
    }

    private static Dictionary<string, object?> ProductPayload((long Id, string Handle, string Name, long PriceInCents) product, bool includePricing)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = product.Id,
            ["name"] = product.Name,
            ["handle"] = product.Handle,
            ["description"] = $"The {product.Name}.",
            ["taxable"] = false,
            ["request_credit_card"] = false,
            ["require_credit_card"] = false,
            ["created_at"] = "2024-01-01T00:00:00-05:00",
            ["updated_at"] = "2024-01-01T00:00:00-05:00"
        };

        if (includePricing)
        {
            payload["price_in_cents"] = product.PriceInCents;
            payload["interval"] = 1;
            payload["interval_unit"] = "month";
            payload["product_price_point_name"] = "Default";
        }

        return payload;
    }

    private static Dictionary<string, object?> CustomerEnvelope(CustomerRecord customer) =>
        new() { ["customer"] = CustomerPayload(customer) };

    private static Dictionary<string, object?> CustomerPayload(CustomerRecord customer)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = customer.Id,
            ["reference"] = customer.Reference,
            ["first_name"] = customer.FirstName,
            ["last_name"] = customer.LastName,
            ["email"] = customer.Email,
            ["created_at"] = "2024-01-01T00:00:00-05:00",
            ["updated_at"] = "2024-01-01T00:00:00-05:00"
        };
    }

    private Dictionary<string, object?> SubscriptionEnvelope(SubscriptionRecord subscription) =>
        new() { ["subscription"] = SubscriptionPayload(subscription) };

    private Dictionary<string, object?> SubscriptionPayload(SubscriptionRecord subscription)
    {
        var customer = _customers.First(c => c.Id == subscription.CustomerId);
        var product = Catalog.First(p => p.Handle == subscription.ProductHandle);
        var periodEnds = DateTimeOffset.UtcNow.AddDays(30).ToString("O");

        return new Dictionary<string, object?>
        {
            ["id"] = subscription.Id,
            ["state"] = subscription.State,
            ["product_price_in_cents"] = product.PriceInCents,
            ["currency"] = "USD",
            ["created_at"] = DateTimeOffset.UtcNow.AddDays(-1).ToString("O"),
            ["updated_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["current_period_started_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["current_period_ends_at"] = periodEnds,
            ["next_assessment_at"] = periodEnds,
            ["balance_in_cents"] = 0,
            ["payment_collection_method"] = "automatic",
            ["product"] = ProductPayload(product, includePricing: true),
            ["customer"] = CustomerPayload(customer)
        };
    }

    private static string ReadBody(HttpRequestMessage request)
    {
        if (request.Content is null)
        {
            return string.Empty;
        }

        return request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object payload)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                payload is string s ? s : JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
    }

    public void Dispose() => _handler.Dispose();

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Responder { get; set; }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Transcript { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Responder is null)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            var response = await Responder(request).ConfigureAwait(false);
            var body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Transcript.Add($"{request.Method} {request.RequestUri} -> {(int)response.StatusCode} {body}");
            return response;
        }
    }
}
