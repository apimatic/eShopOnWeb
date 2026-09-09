using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal enum PaymentCollectionMethod
{
    Automatic,
    Remittance,
    Invoice
}

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionService"/>.
/// Maxio is the billing system of record: the eShopOnWeb user id is stored as the
/// Maxio customer reference, so the user's Maxio customer is always discoverable
/// without local persistence.
///
/// API interactions (per maxio-docs):
/// - GET  /product_families/handle:{handle}/products.json   -> plans
/// - GET  /customers/lookup.json?reference={reference}      -> find customer by app id
/// - POST /customers.json                                   -> create customer (reference is unique)
/// - GET  /customers/{id}/subscriptions.json                -> the customer's subscriptions
/// - POST /subscriptions.json (product_handle, customer_id) -> subscribe
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly MaxioApiClient _api;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    /// <summary>
    /// Subscription states that are not end-of-life. A subscription in any of these
    /// states for the requested plan means the user is already subscribed and the
    /// signup must be treated as idempotent (no duplicate creation).
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "awaiting_signup", "trialing", "assessing", "active",
        "soft_failure", "past_due", "unpaid"
    };

    public MaxioSubscriptionService(
        MaxioApiClient api,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _api = api;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync()
    {
        var familyId = $"handle:{_settings.ProductFamilyHandle}";
        var root = await _api.GetAsync($"/product_families/{familyId}/products.json?per_page=200");

        var plans = new List<SubscriptionPlanDto>();
        foreach (var item in GetItemsArray(root))
        {
            if (!item.TryGetProperty("product", out var product))
            {
                continue;
            }

            // Archived products are not subscribable and should not be offered as plans.
            if (product.TryGetProperty("archived_at", out var archived) &&
                archived.ValueKind == JsonValueKind.String)
            {
                continue;
            }

            plans.Add(MapProduct(product));
        }

        return plans;
    }

    public async Task<(SubscriptionDto, bool)> SubscribeAsync(string userId, string email, string planHandle)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        var plans = await GetPlansAsync();
        var plan = plans.FirstOrDefault(p =>
            string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new InvalidOperationException(
                $"Plan '{planHandle}' was not found in product family '{_settings.ProductFamilyHandle}'.");
        }

        var customer = await EnsureCustomerAsync(userId, email);

        // Idempotency: if the customer already holds a live subscription to this plan,
        // return it instead of creating a duplicate (double-click safe).
        var existing = await FindLiveSubscriptionAsync(customer.id, planHandle);
        if (existing is not null)
        {
            _logger.LogInformation(
                "User {UserId} already has subscription {SubscriptionId} for plan {PlanHandle}; returning existing.",
                userId, existing.Value.id, planHandle);
            return (existing.Value.subscription, true);
        }

        _logger.LogInformation(
            "Creating Maxio subscription for user {UserId} (customer {CustomerId}) on plan {PlanHandle}.",
            userId, customer.id, planHandle);

        // Try the site-default automatic collection first; when the site or product requires a
        // payment method that is not on file, fall back to remittance collection (documented as
        // "an invoice is generated at renewal ... payment recorded manually"), which allows
        // signup without card capture / 3-DS.
        var attempt = await _api.SendAsync(HttpMethod.Post, "/subscriptions.json", SubscriptionPayload(planHandle, customer.id));
        if (attempt.StatusCode == 422 && IsPaymentMethodError(attempt.Body))
        {
            _logger.LogInformation(
                "Site requires a payment method for plan {PlanHandle}; retrying with remittance collection.",
                planHandle);
            attempt = await _api.SendAsync(HttpMethod.Post, "/subscriptions.json",
                SubscriptionPayload(planHandle, customer.id, PaymentCollectionMethod.Remittance));
        }

        if (attempt.StatusCode < 200 || attempt.StatusCode >= 300)
        {
            throw new MaxioApiException(attempt.StatusCode, attempt.Body.GetRawText());
        }

        return (MapSubscription(attempt.Body.GetProperty("subscription")), false);
    }

    private static object SubscriptionPayload(string planHandle, int customerId, PaymentCollectionMethod? collectionMethod = null)
    {
        object subscription = collectionMethod is null
            ? new { product_handle = planHandle, customer_id = customerId }
            : new
            {
                product_handle = planHandle,
                customer_id = customerId,
                payment_collection_method = collectionMethod switch
                {
                    PaymentCollectionMethod.Remittance => "remittance",
                    PaymentCollectionMethod.Invoice => "invoice",
                    _ => "automatic"
                }
            };

        return new { subscription };
    }

    private static bool IsPaymentMethodError(JsonElement errorBody) =>
        errorBody.TryGetProperty("errors", out var errors) &&
        errors.ValueKind == JsonValueKind.Array &&
        errors.EnumerateArray()
            .Any(e => e.ValueKind == JsonValueKind.String &&
                      (e.GetString() ?? string.Empty).Contains("payment method", StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userId)
    {
        var lookup = await _api.SendAsync(
            HttpMethod.Get, $"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}");

        if (lookup.StatusCode == 404)
        {
            // No Maxio customer for this user yet, therefore no subscriptions.
            return Array.Empty<SubscriptionDto>();
        }

        if (lookup.StatusCode < 200 || lookup.StatusCode >= 300)
        {
            throw new MaxioApiException(lookup.StatusCode, lookup.Body.GetRawText());
        }

        var customerId = lookup.Body.GetProperty("customer").GetProperty("id").GetInt32();
        var root = await _api.GetAsync($"/customers/{customerId}/subscriptions.json");

        var subscriptions = new List<SubscriptionDto>();
        foreach (var item in GetItemsArray(root))
        {
            if (item.TryGetProperty("subscription", out var subscription))
            {
                subscriptions.Add(MapSubscription(subscription));
            }
        }

        return subscriptions;
    }

    /// <summary>
    /// Finds the user's Maxio customer by reference (the eShopOnWeb user id), creating it
    /// on first use. The lookup-then-create sequence is idempotent, and the unique-reference
    /// constraint on the Maxio side guarantees a single customer even under concurrent races;
    /// on a reference-conflict rejection the customer is simply looked up again.
    /// </summary>
    private async Task<(int id, JsonElement customer)> EnsureCustomerAsync(string userId, string email)
    {
        var reference = Uri.EscapeDataString(userId);
        var lookup = await _api.SendAsync(HttpMethod.Get, $"/customers/lookup.json?reference={reference}");

        if (lookup.StatusCode >= 200 && lookup.StatusCode < 300)
        {
            var existing = lookup.Body.GetProperty("customer");
            return (existing.GetProperty("id").GetInt32(), existing);
        }

        if (lookup.StatusCode != 404)
        {
            throw new MaxioApiException(lookup.StatusCode, lookup.Body.GetRawText());
        }

        var (firstName, lastName) = SplitName(email);
        var create = await _api.SendAsync(HttpMethod.Post, "/customers.json", new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                reference = userId
            }
        });

        if (create.StatusCode >= 200 && create.StatusCode < 300)
        {
            var created = create.Body.GetProperty("customer");
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.",
                created.GetProperty("id").GetInt32(), userId);
            return (created.GetProperty("id").GetInt32(), created);
        }

        // Lost a create race: the reference is unique, so the customer now exists - re-lookup.
        var relookup = await _api.SendAsync(HttpMethod.Get, $"/customers/lookup.json?reference={reference}");
        if (relookup.StatusCode >= 200 && relookup.StatusCode < 300)
        {
            var existing = relookup.Body.GetProperty("customer");
            return (existing.GetProperty("id").GetInt32(), existing);
        }

        throw new MaxioApiException(create.StatusCode, create.Body.GetRawText());
    }

    private async Task<(int id, SubscriptionDto subscription)?> FindLiveSubscriptionAsync(
        int customerId, string planHandle)
    {
        var root = await _api.GetAsync($"/customers/{customerId}/subscriptions.json");
        foreach (var item in GetItemsArray(root))
        {
            if (!item.TryGetProperty("subscription", out var subscription))
            {
                continue;
            }

            if (!subscription.TryGetProperty("state", out var state) ||
                !LiveStates.Contains(state.GetString() ?? string.Empty))
            {
                continue;
            }

            var productHandle = GetProductHandle(subscription);
            if (string.Equals(productHandle, planHandle, StringComparison.OrdinalIgnoreCase))
            {
                return (subscription.GetProperty("id").GetInt32(), MapSubscription(subscription));
            }
        }

        return null;
    }

    private static string GetProductHandle(JsonElement subscription)
    {
        if (subscription.TryGetProperty("product", out var product) &&
            product.ValueKind == JsonValueKind.Object &&
            product.TryGetProperty("handle", out var handle) &&
            handle.ValueKind == JsonValueKind.String)
        {
            return handle.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static JsonElement[] GetItemsArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToArray();
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("items", out var items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            return items.EnumerateArray().ToArray();
        }

        return Array.Empty<JsonElement>();
    }

    private static SubscriptionPlanDto MapProduct(JsonElement product)
    {
        return new SubscriptionPlanDto
        {
            Handle = GetString(product, "handle") ?? string.Empty,
            Name = GetString(product, "name") ?? string.Empty,
            Description = GetString(product, "description"),
            Price = GetCents(product, "price_in_cents"),
            Interval = GetInt(product, "interval"),
            IntervalUnit = GetString(product, "interval_unit") ?? string.Empty,
            RequireCreditCard = product.TryGetProperty("require_credit_card", out var rcc) &&
                                rcc.ValueKind == JsonValueKind.True,
            CreatedAt = GetDate(product, "created_at") ?? DateTime.MinValue
        };
    }

    private static SubscriptionDto MapSubscription(JsonElement subscription)
    {
        var product = subscription.TryGetProperty("product", out var p) &&
                      p.ValueKind == JsonValueKind.Object
            ? p
            : (JsonElement?)null;

        var price = product is not null
            ? GetCents(product.Value, "price_in_cents")
            : GetCents(subscription, "product_price_in_cents");

        var priceInterval = product is not null
            ? GetInt(product.Value, "interval")
            : 1;
        var priceIntervalUnit = product is not null
            ? GetString(product.Value, "interval_unit") ?? "month"
            : "month";

        return new SubscriptionDto
        {
            Id = GetInt(subscription, "id"),
            State = GetString(subscription, "state") ?? string.Empty,
            PlanHandle = product is not null ? GetString(product.Value, "handle") ?? string.Empty : string.Empty,
            PlanName = product is not null ? GetString(product.Value, "name") ?? string.Empty : string.Empty,
            Price = price,
            Interval = priceInterval,
            IntervalUnit = priceIntervalUnit,
            MaxioCustomerId = subscription.TryGetProperty("customer", out var c) &&
                              c.ValueKind == JsonValueKind.Object &&
                              c.TryGetProperty("id", out var cid) &&
                              cid.ValueKind == JsonValueKind.Number
                ? cid.GetInt32()
                : 0,
            NextBillingDate = GetDate(subscription, "current_period_ends_at"),
            ActivatedAt = GetDate(subscription, "activated_at"),
            CreatedAt = GetDate(subscription, "created_at") ?? DateTime.MinValue
        };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static decimal GetCents(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64() / 100m
            : 0m;

    private static DateTime? GetDate(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    private static (string FirstName, string LastName) SplitName(string? email)
    {
        var local = email;
        var at = email?.IndexOf('@') ?? -1;
        if (local is null)
        {
            return ("eShop", "Customer");
        }

        if (at >= 0)
        {
            local = local[..at];
        }

        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ("eShop", "Customer");
        }

        var first = parts[0];
        var last = parts.Length > 1 ? string.Join(" ", parts[1..]) : "Customer";
        return (first, last);
    }
}
