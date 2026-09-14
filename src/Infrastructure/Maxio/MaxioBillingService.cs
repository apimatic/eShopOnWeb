using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Talks to the Maxio Advanced Billing REST API (https://developers.maxio.com/http/advanced-billing-api).
/// Maxio is the system of record for subscriptions: this service never persists billing state
/// locally, it always reads/writes through to Maxio.
///
/// Endpoints and payload shapes used here were confirmed against Maxio/Chargify's published API
/// reference (Create/Find Customer, Create Subscription, List Subscriptions by Customer, List
/// Products for a Product Family) before writing this client.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    // Subscription states that represent an ongoing enrollment. A repeat "subscribe" for a plan
    // the customer is already in one of these states for is treated as an idempotent no-op.
    // Terminal states (canceled, expired, unpaid) are intentionally excluded so the customer can
    // re-subscribe once a prior enrollment has ended.
    private static readonly HashSet<string> NonTerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "trialing", "assessing", "active", "soft_failure", "past_due", "suspended", "pending"
    };

    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioOptions> options, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var cacheKey = $"maxio:plans:{familyId}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var envelopes = await GetAsync<List<ProductEnvelope>>($"product_families/{familyId}/products.json", cancellationToken);
        var plans = envelopes.Select(e => MapPlan(e.Product)).ToList();

        _cache.Set(cacheKey, (IReadOnlyList<SubscriptionPlan>)plans, CatalogCacheDuration);
        return plans;
    }

    public async Task<SubscriptionPlan?> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await GetAvailablePlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(CustomerSubscription Subscription, bool Created)> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var plan = await GetPlanAsync(planHandle, cancellationToken)
            ?? throw new BillingProviderException($"Plan '{planHandle}' is not part of the configured Maxio product family.");

        var customer = await GetOrCreateCustomerAsync(customerReference, customerEmail, cancellationToken);

        // Idempotency: a customer who already has a non-terminal subscription to this plan gets
        // that enrollment back instead of a second one. This makes double-clicking "Subscribe"
        // (or retrying after a timed-out response) safe.
        var existingSubscriptions = await ListCustomerSubscriptionModelsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s =>
            s.Product is not null &&
            string.Equals(s.Product.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
            NonTerminalStates.Contains(s.State));

        if (existing is not null)
        {
            return (MapSubscription(existing), false);
        }

        // The seeded plans have "payment method not required" (require_credit_card: false), so we
        // subscribe without a card. payment_collection_method "invoice" tells Maxio not to attempt
        // an automatic card charge it has no card to make.
        var body = new CreateSubscriptionRequestBody
        {
            Subscription = new CreateSubscriptionInput
            {
                ProductHandle = plan.Handle,
                CustomerReference = customerReference,
                PaymentCollectionMethod = "invoice"
            },
            UniquenessToken = Guid.NewGuid().ToString("N")
        };

        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", body, _jsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(_jsonOptions, cancellationToken)
            ?? throw new BillingProviderException("Maxio returned an empty response when creating the subscription.");

        return (MapSubscription(envelope.Subscription), true);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var customer = await FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionModelsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(MapSubscription).ToList();
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var handle = _options.ProductFamilyHandle;
        var cacheKey = $"maxio:family-id:{handle}";

        if (_cache.TryGetValue(cacheKey, out int cachedId))
        {
            return cachedId;
        }

        // Product family IDs are reassigned whenever the Maxio site is re-seeded, so we always
        // resolve the current ID from the stable handle rather than configuring/caching it long-term.
        var families = await GetAsync<List<ProductFamilyEnvelope>>("product_families.json", cancellationToken);
        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => string.Equals(f.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new BillingProviderException($"No Maxio product family found with handle '{handle}'.");
        }

        _cache.Set(cacheKey, match.Id, CatalogCacheDuration);
        return match.Id;
    }

    private async Task<CustomerModel?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(_jsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<CustomerModel> GetOrCreateCustomerAsync(string reference, string email, CancellationToken cancellationToken)
    {
        // Idempotent get-or-create: look the customer up by our stable reference first. Maxio
        // enforces reference uniqueness per site, so this - plus the recovery path below - is what
        // guarantees a double-click never creates two Maxio customers for the same eShopOnWeb user.
        var existing = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = email.Split('@')[0];
        var body = new CreateCustomerRequestBody
        {
            Customer = new CustomerAttributesInput
            {
                FirstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart,
                LastName = "Customer",
                Email = email,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", body, _jsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Lost a race with a concurrent request for the same reference (e.g. two near-simultaneous
            // double-clicks). Maxio rejects the duplicate reference; recover by looking up the winner.
            var recovered = await FindCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(_jsonOptions, cancellationToken)
            ?? throw new BillingProviderException("Maxio returned an empty response when creating the customer.");
        return envelope.Customer;
    }

    private async Task<List<SubscriptionModel>> ListCustomerSubscriptionModelsAsync(int customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<SubscriptionEnvelope>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return envelopes.Select(e => e.Subscription).ToList();
    }

    private async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new BillingProviderException($"Maxio returned an empty response for GET {relativeUrl}.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new BillingProviderException($"Maxio API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingProviderException(
                "Maxio is not configured. Set Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle (see MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }
    }

    private static SubscriptionPlan MapPlan(ProductModel product) => new()
    {
        Handle = product.Handle,
        Name = product.Name,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    private static CustomerSubscription MapSubscription(SubscriptionModel subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt
    };

    // Wire models - property names map to Maxio's snake_case JSON via JsonNamingPolicy.SnakeCaseLower.

    private sealed class ProductFamilyEnvelope
    {
        public ProductFamilyModel ProductFamily { get; set; } = default!;
    }

    private sealed class ProductFamilyModel
    {
        public int Id { get; set; }
        public string Handle { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ProductEnvelope
    {
        public ProductModel Product { get; set; } = default!;
    }

    private sealed class ProductModel
    {
        public int Id { get; set; }
        public string Handle { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int PriceInCents { get; set; }
        public int Interval { get; set; }
        public string IntervalUnit { get; set; } = string.Empty;
        public bool RequireCreditCard { get; set; }
    }

    private sealed class CustomerEnvelope
    {
        public CustomerModel Customer { get; set; } = default!;
    }

    private sealed class CustomerModel
    {
        public int Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    private sealed class CustomerAttributesInput
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }

    private sealed class CreateCustomerRequestBody
    {
        public CustomerAttributesInput Customer { get; set; } = default!;
    }

    private sealed class SubscriptionEnvelope
    {
        public SubscriptionModel Subscription { get; set; } = default!;
    }

    private sealed class SubscriptionModel
    {
        public long Id { get; set; }
        public string State { get; set; } = string.Empty;
        public int ProductPriceInCents { get; set; }
        public DateTimeOffset? NextAssessmentAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public ProductModel? Product { get; set; }
        public CustomerModel? Customer { get; set; }
    }

    private sealed class CreateSubscriptionInput
    {
        public string ProductHandle { get; set; } = string.Empty;
        public string CustomerReference { get; set; } = string.Empty;
        public string PaymentCollectionMethod { get; set; } = "invoice";
    }

    private sealed class CreateSubscriptionRequestBody
    {
        public CreateSubscriptionInput Subscription { get; set; } = default!;
        public string UniquenessToken { get; set; } = string.Empty;
    }
}
