using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// </summary>
/// <remarks>
/// Maxio is the system of record: nothing is mirrored locally, so every answer reflects live billing
/// state and the integration survives a host that keeps its own data in memory.
/// </remarks>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// How many times a subscribe attempt may recompute its reference and try again. Each extra round
    /// only happens when a concurrent writer took the reference we derived, which resolves in one.
    /// </summary>
    private const int MaxSubscribeRounds = 3;

    private const string SiteCacheKey = "maxio:site";
    private static readonly TimeSpan SiteCacheDuration = TimeSpan.FromMinutes(10);

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly SubscriberGate _gate;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioSettings> settings,
        SubscriberGate gate,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _gate = gate;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var settings = RequireConfiguredSettings();
        var familyHandle = settings.ProductFamilyHandle!;

        var products = await TranslateFailuresAsync(
            () => _client.ListProductsForFamilyAsync(familyHandle, cancellationToken),
            $"listing plans in product family '{familyHandle}'").ConfigureAwait(false);

        var currency = await TryGetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Handle, StringComparer.Ordinal)
            .Select(p => MapPlan(p, currency, familyHandle))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var settings = RequireConfiguredSettings();

        // Resolve the handle against the configured product family first. This both gives the caller
        // a precise 404 instead of a provider error, and stops a deployment from enrolling shoppers
        // into products outside the catalog it was configured for.
        var plan = await ResolvePlanAsync(planHandle, cancellationToken).ConfigureAwait(false);

        using var _ = await _gate.AcquireAsync(subscriber.BillingReference, cancellationToken).ConfigureAwait(false);

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);
        var customerReference = customer.Reference ?? subscriber.BillingReference;

        for (var round = 1; round <= MaxSubscribeRounds; round++)
        {
            var existing = await TranslateFailuresAsync(
                () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
                "reading existing subscriptions").ConfigureAwait(false);

            var onThisPlan = existing
                .Where(s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var live = onThisPlan
                .Where(s => MaxioSubscriptionStates.IsLive(s.State))
                .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            if (live is not null)
            {
                _logger.LogInformation(
                    "Shopper {CustomerReference} is already subscribed to {PlanHandle} (subscription {SubscriptionId}, state {State}).",
                    customerReference, plan.Handle, live.Id, live.State);

                return SubscribeResult.AlreadySubscribed(MapSubscription(live));
            }

            var reference = idempotencyKey is null
                ? BillingReferences.ForSubscription(customerReference, plan.Handle, onThisPlan.Count)
                : BillingReferences.ForSubscription(customerReference, plan.Handle, idempotencyKey);

            if (idempotencyKey is not null)
            {
                // With an explicit key the reference *is* the identity of the request, so a record
                // already stamped with it settles the call whatever state it reached.
                var replay = await FindOwnedSubscriptionAsync(reference, customer.Id, cancellationToken).ConfigureAwait(false);
                if (replay is not null)
                {
                    return SubscribeResult.AlreadySubscribed(MapSubscription(replay));
                }
            }

            try
            {
                var created = await TranslateFailuresAsync(
                    () => _client.CreateSubscriptionAsync(
                        new MaxioSubscriptionAttributes
                        {
                            ProductHandle = plan.Handle,
                            CustomerId = customer.Id,
                            PaymentCollectionMethod = settings.PaymentCollectionMethod,
                            Reference = reference
                        },
                        cancellationToken),
                    $"subscribing to plan '{plan.Handle}'",
                    rethrowReferenceConflict: true).ConfigureAwait(false);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} ({Reference}) on plan {PlanHandle} for {CustomerReference}.",
                    created.Id, reference, plan.Handle, customerReference);

                return SubscribeResult.NewlyCreated(MapSubscription(created));
            }
            catch (MaxioApiException ex) when (ex.IsReferenceAlreadyTaken)
            {
                // Another request — a double click, a client retry, or a second instance — got there
                // first. Adopt whatever it produced rather than creating a second subscription.
                var adopted = await FindOwnedSubscriptionAsync(reference, customer.Id, cancellationToken).ConfigureAwait(false);

                if (adopted is not null && (idempotencyKey is not null || MaxioSubscriptionStates.IsLive(adopted.State)))
                {
                    _logger.LogInformation(
                        "Adopted concurrently created subscription {SubscriptionId} ({Reference}) for {CustomerReference}.",
                        adopted.Id, reference, customerReference);

                    return SubscribeResult.AlreadySubscribed(MapSubscription(adopted));
                }

                if (idempotencyKey is not null)
                {
                    // The key maps to a reference we cannot claim and cannot recompute.
                    throw new BillingProviderException(
                        "The supplied idempotency key is already in use by a different subscription.",
                        (int?)ex.StatusCode,
                        ex.Errors,
                        isCallerError: true,
                        innerException: ex);
                }

                _logger.LogWarning(
                    "Subscription reference {Reference} was taken by a subscription that is no longer live; recomputing (round {Round}/{MaxRounds}).",
                    reference, round, MaxSubscribeRounds);
            }
        }

        throw new BillingProviderException(
            $"Could not establish a subscription to '{plan.Handle}' after {MaxSubscribeRounds} attempts because the derived reference kept being taken. Retry the request.");
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        RequireConfiguredSettings();

        var customer = await TranslateFailuresAsync(
            () => _client.FindCustomerByReferenceAsync(subscriber.BillingReference, cancellationToken),
            "looking up the billing customer").ConfigureAwait(false);

        if (customer is null)
        {
            // A shopper who has never subscribed simply has no billing customer yet.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await TranslateFailuresAsync(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            "listing subscriptions").ConfigureAwait(false);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle ?? string.Empty);
        }

        var plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);

        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle.Trim());
    }

    /// <summary>
    /// Returns the Maxio customer for this shopper, creating one on first use.
    /// The lookup-then-create pair is racy by nature, so the create is written to survive losing that
    /// race: Maxio enforces reference uniqueness, and a rejection just means someone else won.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var reference = subscriber.BillingReference;

        var existing = await TranslateFailuresAsync(
            () => _client.FindCustomerByReferenceAsync(reference, cancellationToken),
            "looking up the billing customer").ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await TranslateFailuresAsync(
                () => _client.CreateCustomerAsync(
                    new MaxioCustomerAttributes
                    {
                        FirstName = subscriber.FirstName,
                        LastName = subscriber.LastName,
                        Email = subscriber.Email,
                        Reference = reference
                    },
                    cancellationToken),
                "creating the billing customer",
                rethrowReferenceConflict: true).ConfigureAwait(false);

            _logger.LogInformation("Created Maxio customer {CustomerId} ({Reference}).", created.Id, reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsReferenceAlreadyTaken)
        {
            var raced = await TranslateFailuresAsync(
                () => _client.FindCustomerByReferenceAsync(reference, cancellationToken),
                "looking up the billing customer").ConfigureAwait(false);

            return raced ?? throw new BillingProviderException(
                "Maxio reports the billing customer reference is taken but will not return the customer.",
                (int?)ex.StatusCode,
                ex.Errors,
                innerException: ex);
        }
    }

    /// <summary>
    /// Looks a subscription up by reference and confirms it belongs to <paramref name="customerId"/>.
    /// </summary>
    private async Task<MaxioSubscription?> FindOwnedSubscriptionAsync(
        string reference,
        long customerId,
        CancellationToken cancellationToken)
    {
        var found = await TranslateFailuresAsync(
            () => _client.FindSubscriptionByReferenceAsync(reference, cancellationToken),
            "looking up a subscription by reference").ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        // References are derived from the shopper's own customer reference, so a match already implies
        // ownership; the explicit check is belt and braces for when Maxio echoes the customer back.
        if (found.Customer is not null && found.Customer.Id != customerId)
        {
            _logger.LogWarning(
                "Subscription reference {Reference} resolves to customer {OtherCustomerId}, not {CustomerId}; refusing to adopt it.",
                reference, found.Customer.Id, customerId);

            return null;
        }

        return found;
    }

    /// <summary>
    /// Reads the site's primary currency so plan prices can be quoted with a unit.
    /// A failure here degrades the answer rather than the call: currency is presentational, and the
    /// authoritative currency of a sale is echoed on the subscription itself.
    /// </summary>
    private async Task<string?> TryGetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string?>(SiteCacheKey, out var cached))
        {
            return cached;
        }

        string? currency = null;

        try
        {
            var site = await _client.GetSiteAsync(cancellationToken).ConfigureAwait(false);
            currency = site.Currency;
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning(ex, "Could not read Maxio site metadata; plan prices will omit a currency.");
        }

        _cache.Set(SiteCacheKey, currency, SiteCacheDuration);
        return currency;
    }

    private MaxioSettings RequireConfiguredSettings()
    {
        var settings = _settings.CurrentValue;

        if (!settings.IsConfigured)
        {
            throw new BillingNotConfiguredException(
                "Subscription billing is not configured. Missing configuration: " +
                string.Join(", ", settings.DescribeMissingSettings()) +
                ". Supply these via user-secrets or the environment; never commit their values.");
        }

        return settings;
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, string? currency, string familyHandle) =>
        new(
            handle: product.Handle!,
            name: product.Name ?? product.Handle!,
            description: product.Description,
            priceInCents: product.PriceInCents,
            currency: currency,
            interval: product.Interval,
            intervalUnit: product.IntervalUnit ?? "month",
            productFamilyHandle: product.ProductFamily?.Handle ?? familyHandle,
            requiresPaymentMethod: product.RequireCreditCard,
            trialInterval: product.TrialInterval,
            trialIntervalUnit: product.TrialIntervalUnit);

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) =>
        new(
            id: subscription.Id,
            reference: subscription.Reference,
            state: subscription.State ?? "unknown",
            isLive: MaxioSubscriptionStates.IsLive(subscription.State),
            planHandle: subscription.Product?.Handle ?? string.Empty,
            planName: subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            priceInCents: subscription.ProductPriceInCents,
            currency: subscription.Currency,
            interval: subscription.Product?.Interval ?? 0,
            intervalUnit: subscription.Product?.IntervalUnit ?? string.Empty,
            balanceInCents: subscription.BalanceInCents,
            paymentCollectionMethod: subscription.PaymentCollectionMethod,
            customerId: subscription.Customer?.Id ?? 0,
            customerReference: subscription.Customer?.Reference,
            createdAt: subscription.CreatedAt,
            activatedAt: subscription.ActivatedAt,
            currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextBillingAt: subscription.NextAssessmentAt,
            trialEndedAt: subscription.TrialEndedAt,
            canceledAt: subscription.CanceledAt);

    /// <summary>
    /// Runs a Maxio call and converts provider-specific failures into the provider-agnostic
    /// <see cref="BillingProviderException"/> the rest of the application handles.
    /// </summary>
    /// <param name="rethrowReferenceConflict">
    /// When true, a "reference already taken" rejection passes through untranslated so the caller can
    /// run its create-or-adopt recovery.
    /// </param>
    private async Task<T> TranslateFailuresAsync<T>(
        Func<Task<T>> operation,
        string what,
        bool rethrowReferenceConflict = false)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (MaxioApiException ex) when (!(rethrowReferenceConflict && ex.IsReferenceAlreadyTaken))
        {
            _logger.LogError(ex, "Maxio failed while {What}.", what);

            throw new BillingProviderException(
                $"The billing provider failed while {what}.",
                (int?)ex.StatusCode,
                ex.Errors,
                ex.IsCallerError,
                ex);
        }
    }
}
