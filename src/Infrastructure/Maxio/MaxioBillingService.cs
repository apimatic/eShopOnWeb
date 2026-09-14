using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// HTTP implementation of <see cref="IMaxioBillingService"/> talking to the Maxio
/// Advanced Billing (Chargify) REST API. Maxio is the system of record; nothing
/// billing-related is persisted locally.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    private const string FamilyIdCacheKey = "maxio:product-family-id";

    // Maxio subscription states that are still "live" for idempotency purposes:
    // a repeat subscribe to the same plan while one of these is active returns the
    // existing subscription instead of enrolling again.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "pending", "past_due", "soft_failure", "on_hold"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Serializes subscribe operations per user reference so a genuine concurrent
    // double-submit (not just a sequential double-click) cannot create two
    // customers/subscriptions in the read-then-write window.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioSettings> settings,
        IMemoryCache cache, IAppLogger<MaxioBillingService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        var products = await GetAsync<List<ProductEnvelope>>(
            $"product_families/{familyId}/products.json", cancellationToken)
            ?? new List<ProductEnvelope>();

        return products
            .Select(e => e.Product)
            .Where(p => p is not null && p.ArchivedAt is null)
            .Select(MapPlan!)
            .ToList();
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName,
        string lastName, CancellationToken cancellationToken = default)
    {
        var existing = await LookupCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return MapCustomer(existing);
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new CustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var response = await _httpClient.PostAsJsonAsync("customers.json", createRequest, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Reference uniqueness is enforced by Maxio. A 422 here means a
            // concurrent request created the customer first — re-look it up so the
            // outcome is idempotent rather than an error.
            var raced = await LookupCustomerAsync(reference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Maxio customer for reference {0} already existed (resolved a create race).", reference);
                return MapCustomer(raced);
            }
        }

        await EnsureSuccessAsync(response, "create customer", cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        var customer = envelope?.Customer
            ?? throw new MaxioBillingException("Maxio returned an empty customer on create.");

        _logger.LogInformation("Created Maxio customer {0} for reference {1}.", customer.Id, reference);
        return MapCustomer(customer);
    }

    public async Task<Subscription> SubscribeAsync(string customerReference, string email, string firstName,
        string lastName, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new PlanNotFoundException(planHandle ?? string.Empty);
        }

        // Validate the plan is one we actually offer (present in the configured family).
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new PlanNotFoundException(planHandle);

        var gate = SubscribeLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(customerReference, email, firstName, lastName, cancellationToken);

            // Idempotency: if the customer already has a live subscription to this
            // plan, return it instead of enrolling twice.
            var current = await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var duplicate = current.FirstOrDefault(s =>
                string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase) &&
                LiveStates.Contains(s.State));
            if (duplicate is not null)
            {
                duplicate.AlreadyExisted = true;
                _logger.LogInformation("Customer {0} already subscribed to {1} (subscription {2}); returning existing.",
                    customer.Id, plan.Handle, duplicate.Id);
                return duplicate;
            }

            var createRequest = new CreateSubscriptionRequest
            {
                Subscription = new SubscriptionAttributes
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = "remittance"
                }
            };

            using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", createRequest, JsonOptions, cancellationToken);
            await EnsureSuccessAsync(response, "create subscription", cancellationToken);

            var envelope = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
            var subscription = envelope?.Subscription
                ?? throw new MaxioBillingException("Maxio returned an empty subscription on create.");

            _logger.LogInformation("Created Maxio subscription {0} for customer {1} on plan {2}.",
                subscription.Id, customer.Id, plan.Handle);
            return MapSubscription(subscription);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string reference,
        CancellationToken cancellationToken = default)
    {
        var customer = await LookupCustomerAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    // ---- internals -------------------------------------------------------

    private async Task<long> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(FamilyIdCacheKey, out long cached))
        {
            return cached;
        }

        var families = await GetAsync<List<ProductFamilyEnvelope>>("product_families.json", cancellationToken)
            ?? new List<ProductFamilyEnvelope>();

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                string.Equals(f.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new MaxioBillingException(
                $"Configured product family handle '{_settings.ProductFamilyHandle}' was not found on the Maxio site.");
        }

        _cache.Set(FamilyIdCacheKey, match.Id, TimeSpan.FromMinutes(10));
        return match.Id;
    }

    private async Task<CustomerWire?> LookupCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var uri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "look up customer", cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return envelope?.Customer;
    }

    private async Task<List<Subscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<SubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken)
            ?? new List<SubscriptionEnvelope>();

        return envelopes
            .Select(e => e.Subscription)
            .Where(s => s is not null)
            .Select(MapSubscription!)
            .ToList();
    }

    private async Task<T?> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(relativeUri, cancellationToken);
        await EnsureSuccessAsync(response, $"GET {relativeUri}", cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(body, JsonOptions);
                detail = parsed?.Errors is { Count: > 0 }
                    ? string.Join("; ", parsed.Errors)
                    : body;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with no parsed detail.
        }

        var message = $"Maxio request failed to {operation} (HTTP {(int)response.StatusCode})"
            + (detail is null ? "." : $": {detail}");
        _logger.LogWarning(message);
        throw new MaxioBillingException(message, (int)response.StatusCode);
    }

    private SubscriptionPlan MapPlan(ProductWire p) => new()
    {
        Handle = p.Handle ?? string.Empty,
        Name = p.Name ?? string.Empty,
        Description = p.Description,
        ProductFamilyHandle = p.ProductFamily?.Handle ?? _settings.ProductFamilyHandle,
        PriceInCents = p.PriceInCents,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit ?? string.Empty
    };

    private static MaxioCustomer MapCustomer(CustomerWire c) => new()
    {
        Id = c.Id,
        Reference = c.Reference ?? string.Empty,
        Email = c.Email ?? string.Empty,
        FirstName = c.FirstName ?? string.Empty,
        LastName = c.LastName ?? string.Empty
    };

    private static Subscription MapSubscription(SubscriptionWire s) => new()
    {
        Id = s.Id,
        State = s.State ?? string.Empty,
        PlanHandle = s.Product?.Handle ?? string.Empty,
        PlanName = s.Product?.Name ?? string.Empty,
        PriceInCents = s.Product?.PriceInCents ?? 0,
        Interval = s.Product?.Interval ?? 0,
        IntervalUnit = s.Product?.IntervalUnit ?? string.Empty,
        NextBillingDate = s.CurrentPeriodEndsAt ?? s.NextAssessmentAt,
        CreatedAt = s.CreatedAt,
        PaymentCollectionMethod = s.PaymentCollectionMethod ?? string.Empty
    };
}
