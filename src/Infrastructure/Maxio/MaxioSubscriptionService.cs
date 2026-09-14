using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio-backed implementation of <see cref="ISubscriptionService"/>. Orchestrates the subscribe
/// hero flow (ensure customer -> ensure subscription) idempotently and projects Maxio resources
/// onto the billing-system-agnostic domain models.
/// </summary>
internal sealed class MaxioSubscriptionService : ISubscriptionService
{
    private const string SiteCurrencyCacheKey = "maxio:site-currency";

    // Subscription states that are terminal — a customer in one of these is eligible to
    // (re)subscribe to the same plan. Any other state is treated as an active enrollment, so we
    // return it instead of creating a duplicate (idempotency / double-click safety).
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended",
    };

    // Serializes ensure-customer + ensure-subscription per shopper so concurrent requests (e.g. a
    // double-clicked Subscribe button) cannot race into two customers or two subscriptions.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ReferenceLocks = new();

    // Bill by invoice (remittance) rather than attempting an automatic charge: these plans require
    // no payment method and this integration captures no card, so an automatic collection attempt
    // would fail with "no payment method on file". See Collection-Method.yaml in the spec.
    private const string PaymentCollectionMethod = "remittance";

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _client.ListProductsAsync(cancellationToken);
        var currency = await ResolveSiteCurrencyAsync(cancellationToken);

        return products
            .Where(IsOfferedPlan)
            .OrderBy(p => p.PriceInCents)
            .Select(p => MapPlan(p, currency))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));
        if (string.IsNullOrWhiteSpace(subscriber.Reference))
            throw new ArgumentException("Subscriber reference is required.", nameof(subscriber));
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new ArgumentException("Plan handle is required.", nameof(planHandle));

        // Validate the plan is one we actually offer before touching the customer.
        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var gate = ReferenceLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Idempotency: if the shopper already has a live subscription to this plan, return it.
            var existing = await FindActiveSubscriptionAsync(customer.Id, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscribe is a no-op: customer {CustomerId} already has {State} subscription {SubscriptionId} for plan {PlanHandle}.",
                    customer.Id, existing.State, existing.Id, planHandle);
                return new SubscribeResult(MapSubscription(existing), alreadyExisted: true);
            }

            var created = await _client.CreateSubscriptionAsync(
                new CreateSubscriptionDto
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = PaymentCollectionMethod,
                },
                cancellationToken);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {PlanHandle}.",
                created.Id, created.State, customer.Id, planHandle);

            return new SubscribeResult(MapSubscription(created), alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        if (subscriber is null) throw new ArgumentNullException(nameof(subscriber));
        if (string.IsNullOrWhiteSpace(subscriber.Reference))
            throw new ArgumentException("Subscriber reference is required.", nameof(subscriber));

        var customer = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            // Never subscribed -> no billing customer yet.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(MapSubscription)
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    /// <summary>
    /// Returns the existing Maxio customer for the shopper's reference, creating one if needed.
    /// Guards against a create/create race by re-looking up on the unique-reference (422) error.
    /// </summary>
    private async Task<MaxioCustomerDto> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);
        try
        {
            var created = await _client.CreateCustomerAsync(
                new CreateCustomerDto
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = subscriber.Email,
                    Reference = subscriber.Reference,
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.",
                created.Id, subscriber.Reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Most likely a concurrent request already created the customer with this unique
            // reference. Re-look it up; if it's really there, use it.
            _logger.LogWarning(ex, "Create customer for reference {Reference} was rejected; re-checking for an existing customer.",
                subscriber.Reference);
            var recheck = await _client.LookupCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (recheck is not null)
            {
                return recheck;
            }

            throw;
        }
    }

    private async Task<MaxioSubscriptionDto?> FindActiveSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal)
            && !IsTerminal(s.State));
    }

    private bool IsOfferedPlan(MaxioProductDto product) =>
        product.ArchivedAt is null
        && string.Equals(product.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(product.Handle);

    private static bool IsTerminal(string? state) =>
        state is not null && TerminalStates.Contains(state);

    private async Task<string> ResolveSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCurrencyCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        // Site currency is stable for the life of a site; cache it to avoid a call per plan listing.
        var currency = await _client.GetSiteCurrencyAsync(cancellationToken) ?? string.Empty;
        _cache.Set(SiteCurrencyCacheKey, currency, TimeSpan.FromHours(12));
        return currency;
    }

    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();
        if (!string.IsNullOrEmpty(first) || !string.IsNullOrEmpty(last))
        {
            return (string.IsNullOrEmpty(first) ? "eShopOnWeb" : first!, string.IsNullOrEmpty(last) ? "Subscriber" : last!);
        }

        // Fall back to the local part of the email so the customer record is human-readable.
        var local = subscriber.Email;
        var at = local?.IndexOf('@') ?? -1;
        if (at > 0)
        {
            local = subscriber.Email!.Substring(0, at);
        }

        return (string.IsNullOrWhiteSpace(local) ? "eShopOnWeb" : local!, "Subscriber");
    }

    private SubscriptionPlan MapPlan(MaxioProductDto product, string currency) => new(
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: product.PriceInCents,
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? string.Empty,
        currency: currency);

    private static CustomerSubscription MapSubscription(MaxioSubscriptionDto s) => new(
        id: s.Id,
        state: s.State ?? "unknown",
        planHandle: s.Product?.Handle ?? string.Empty,
        planName: s.Product?.Name ?? string.Empty,
        productPriceInCents: s.ProductPriceInCents,
        currency: s.Currency ?? string.Empty,
        currentPeriodEndsAt: ParseDate(s.CurrentPeriodEndsAt),
        nextAssessmentAt: ParseDate(s.NextAssessmentAt),
        activatedAt: ParseDate(s.ActivatedAt),
        createdAt: ParseDate(s.CreatedAt));

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
