using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// An in-memory stand-in for the Maxio Advanced Billing API used to test the
/// billing service logic. Implements the subset of the spec the integration
/// uses, including the unique-reference rule for customers and subscriptions.
/// </summary>
public sealed class FakeMaxioHandler : HttpMessageHandler
{
    public const string ProductFamilyHandle = "eshop-subscribe";

    public sealed class FakeSubscription
    {
        public long Id { get; set; }
        public string State { get; set; } = "active";
        public string? Reference { get; set; }
        public string ProductHandle { get; set; } = string.Empty;
        public long CustomerId { get; set; }
    }

    private sealed record FakeCustomer(long Id, string? Reference, string Email);

    private long _nextSubscriptionId = 9000;
    private long _nextCustomerId = 5000;

    private readonly ConcurrentDictionary<long, FakeSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<string, long> _customersByReference = new();
    private readonly ConcurrentDictionary<long, FakeCustomer> _customersById = new();

    public int CustomerCreateCalls { get; private set; }
    public int SubscriptionCreateCalls { get; private set; }

    public IReadOnlyCollection<FakeSubscription> Subscriptions => _subscriptions.Values.ToList();

    public FakeMaxioHandler WithExistingCustomer(string reference)
    {
        var id = _nextCustomerId++;
        _customersByReference[reference] = id;
        _customersById[id] = new FakeCustomer(id, reference, $"{reference}@example.com");
        return this;
    }

    public void SetSubscriptionState(long subscriptionId, string state)
    {
        _subscriptions[subscriptionId].State = state;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/').TrimEnd('/');
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^5];
        }
        var query = ParseQuery(request.RequestUri.Query);

        switch ((request.Method.Method.ToUpperInvariant(), path))
        {
            case ("GET", "product_families"):
                return Json(new[]
                {
                    new { product_family = new { id = 42L, name = "eShopSubscribe", handle = ProductFamilyHandle } }
                });

            case ("GET", var p) when p.StartsWith("product_families/") && p.EndsWith("/products"):
                return Json(new[]
                {
                    new
                    {
                        product = new
                        {
                            id = 1L,
                            name = "Pro Plan",
                            handle = "eshop-pro",
                            description = "The pro plan",
                            price_in_cents = 29900L,
                            interval = 1,
                            interval_unit = "month",
                            require_credit_card = false,
                            archived_at = (string?)null,
                            product_family = new { id = 42L, handle = ProductFamilyHandle }
                        }
                    },
                    new
                    {
                        product = new
                        {
                            id = 3L,
                            name = "Basic Plan",
                            handle = "basic-plan",
                            description = "The basic plan",
                            price_in_cents = 2900L,
                            interval = 1,
                            interval_unit = "month",
                            require_credit_card = false,
                            archived_at = (string?)null,
                            product_family = new { id = 42L, handle = ProductFamilyHandle }
                        }
                    },
                    new
                    {
                        product = new
                        {
                            id = 2L,
                            name = "Old Plan",
                            handle = "old-plan",
                            description = "",
                            price_in_cents = 100L,
                            interval = 1,
                            interval_unit = "month",
                            require_credit_card = false,
                            archived_at = "2020-01-01T00:00:00-05:00",
                            product_family = new { id = 42L, handle = ProductFamilyHandle }
                        }
                    }
                });

            case ("GET", "customers/lookup"):
            {
                var reference = query["reference"];
                return _customersByReference.TryGetValue(reference, out var id)
                    ? Json(new { customer = CustomerPayload(id) })
                    : NotFound();
            }

            case ("POST", "customers"):
            {
                CustomerCreateCalls++;
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var customer = doc.RootElement.GetProperty("customer");
                var reference = customer.TryGetProperty("reference", out var r) && r.ValueKind != JsonValueKind.Null
                    ? r.GetString()!
                    : string.Empty;

                if (reference.Length > 0 && _customersByReference.ContainsKey(reference))
                {
                    return Json(new { errors = new[] { "Reference: must be unique - that value has been taken." } },
                        HttpStatusCode.UnprocessableEntity);
                }

                var id = _nextCustomerId++;
                _customersById[id] = new FakeCustomer(id, reference, customer.GetProperty("email").GetString()!);
                if (reference.Length > 0)
                {
                    _customersByReference[reference] = id;
                }
                return Json(new { customer = CustomerPayload(id) }, HttpStatusCode.Created);
            }

            case ("GET", "subscriptions/lookup"):
            {
                var reference = query["reference"];
                var sub = _subscriptions.Values.FirstOrDefault(s => s.Reference == reference);
                return sub is null ? NotFound() : Json(new { subscription = SubscriptionPayload(sub) });
            }

            case ("POST", "subscriptions"):
            {
                SubscriptionCreateCalls++;
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var subscription = doc.RootElement.GetProperty("subscription");
                var reference = subscription.TryGetProperty("reference", out var r) && r.ValueKind != JsonValueKind.Null
                    ? r.GetString()!
                    : string.Empty;

                if (reference.Length > 0 && _subscriptions.Values.Any(s => s.Reference == reference))
                {
                    return Json(new { errors = new[] { "Reference: must be unique - that value has been taken." } },
                        HttpStatusCode.UnprocessableEntity);
                }

                var sub = new FakeSubscription
                {
                    Id = _nextSubscriptionId++,
                    Reference = reference,
                    ProductHandle = subscription.GetProperty("product_handle").GetString()!,
                    CustomerId = subscription.GetProperty("customer_id").GetInt64(),
                    State = "active"
                };
                _subscriptions[sub.Id] = sub;
                return Json(new { subscription = SubscriptionPayload(sub) }, HttpStatusCode.Created);
            }

            case ("GET", var p) when p.StartsWith("customers/") && p.EndsWith("/subscriptions"):
            {
                var customerId = long.Parse(p.Split('/')[1]);
                var subs = _subscriptions.Values
                    .Where(s => s.CustomerId == customerId)
                    .Select(s => new { subscription = SubscriptionPayload(s) })
                    .ToList();
                return Json(subs);
            }

            default:
                return NotFound();
        }
    }

    private object CustomerPayload(long id) => new
    {
        id,
        first_name = "First",
        last_name = "Last",
        email = _customersById[id].Email,
        reference = _customersById[id].Reference
    };

    private object SubscriptionPayload(FakeSubscription sub)
    {
        var (price, name) = sub.ProductHandle switch
        {
            "eshop-pro" => (29900L, "Pro Plan"),
            _ => (2900L, "Basic Plan")
        };
        return new
        {
            id = sub.Id,
            state = sub.State,
            reference = sub.Reference,
            product_price_in_cents = price,
            current_period_ends_at = "2026-10-09T13:32:55+05:00",
            next_assessment_at = "2026-10-09T13:32:55+05:00",
            activated_at = "2026-09-09T13:32:58+05:00",
            created_at = "2026-09-09T13:32:55+05:00",
            product = new { id = 1L, name, handle = sub.ProductHandle, price_in_cents = price },
            customer = new { id = sub.CustomerId }
        };
    }

    private static HttpResponseMessage Json(object payload, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage NotFound() =>
        new(HttpStatusCode.NotFound) { Content = new StringContent("Not Found") };

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in (query.StartsWith('?') ? query[1..] : query).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            result[Uri.UnescapeDataString(parts[0])] = value;
        }
        return result;
    }
}
