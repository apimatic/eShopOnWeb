using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Internal;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing, which is the system of record: nothing
/// about a shopper's subscription is mirrored into the eShopOnWeb database.
/// <para>
/// Idempotency comes from references rather than local state. The customer reference is a pure
/// function of the shopper's login and the subscription reference a pure function of that plus the
/// plan handle, both enforced unique by Maxio. A repeated subscribe therefore resolves to the same
/// subscription whether the repeat arrives a millisecond or a restart later.
/// </para>
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>How many times a shopper may re-subscribe to the same plan after earlier ones ended.</summary>
    private const int MaxResubscribeSlots = 50;

    private const string PlanCacheKeyPrefix = "maxio:plans:";
    private const string SiteCacheKey = "maxio:site";

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscriberLocks;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        KeyedAsyncLock subscriberLocks,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _subscriberLocks = subscriberLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireProductFamilyHandle();
        var cacheKey = PlanCacheKeyPrefix + familyHandle;

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var plans = await LoadPlansAsync(familyHandle, cancellationToken).ConfigureAwait(false);
        _cache.Set(cacheKey, plans, _settings.CatalogCacheDuration);
        return plans;
    }

    public async Task<SubscriptionPlan?> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var plans = await GetPlansAsync(cancellationToken).ConfigureAwait(false);
        var match = FindPlan(plans, planHandle);
        if (match is not null)
        {
            return match;
        }

        // A miss may just mean the catalog changed since it was cached — a plan added minutes ago
        // must be subscribable now, not after the cache expires. Re-read once before giving up.
        var familyHandle = RequireProductFamilyHandle();
        var fresh = await LoadPlansAsync(familyHandle, cancellationToken).ConfigureAwait(false);
        _cache.Set(PlanCacheKeyPrefix + familyHandle, fresh, _settings.CatalogCacheDuration);
        return FindPlan(fresh, planHandle);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        var plan = await GetPlanAsync(planHandle, cancellationToken).ConfigureAwait(false)
                   ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customerReference = MaxioReference.ForCustomer(_settings.ReferencePrefix, subscriber.StableKey);

        // Serialise concurrent subscribe attempts for the same shopper so a double-click cannot
        // produce two customers or two subscriptions.
        using (await _subscriberLocks.AcquireAsync(customerReference, cancellationToken).ConfigureAwait(false))
        {
            var customer = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken).ConfigureAwait(false);
            return await EnsureSubscriptionAsync(customer, customerReference, plan, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        var customerReference = MaxioReference.ForCustomer(_settings.ReferencePrefix, subscriber.StableKey);
        var customer = await Guarded(
            () => _client.FindCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up the billing customer").ConfigureAwait(false);

        if (customer is null)
        {
            // No billing customer yet simply means the shopper has never subscribed.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await Guarded(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            "list the shopper's subscriptions").ConfigureAwait(false);

        var currency = await GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

        return subscriptions
            .Select(subscription => Map(subscription, currency))
            .OrderByDescending(subscription => subscription.IsLive)
            .ThenByDescending(subscription => subscription.ActivatedAt ?? subscription.CurrentPeriodStartedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await Guarded(
            () => _client.FindCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up the billing customer").ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var attributes = new MaxioCustomerAttributes
        {
            FirstName = subscriber.FirstName,
            LastName = subscriber.LastName,
            Email = subscriber.Email,
            Reference = customerReference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(attributes, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, customerReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsReferenceTaken)
        {
            // Another caller (or another instance) created the customer between our lookup and our
            // create. That is the expected outcome of a race, not a failure — adopt their record.
            _logger.LogInformation(
                "Maxio customer {CustomerReference} was created concurrently; adopting the existing record.", customerReference);

            return await Guarded(
                       () => _client.FindCustomerByReferenceAsync(customerReference, cancellationToken),
                       "re-read the billing customer after a create race").ConfigureAwait(false)
                   ?? throw new SubscriptionBillingException(
                       $"Maxio reported customer reference '{customerReference}' as taken but did not return it.",
                       (int)ex.StatusCode);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "create the billing customer");
        }
    }

    private async Task<SubscribeResult> EnsureSubscriptionAsync(
        MaxioCustomer customer,
        string customerReference,
        SubscriptionPlan plan,
        CancellationToken cancellationToken)
    {
        var currency = await GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);
        var slot = 0;

        // Bounded: each iteration either returns, advances to the next slot, or retries a slot it
        // lost a race on — so the loop cannot spin.
        for (var iteration = 0; iteration <= (MaxResubscribeSlots * 2) + 2; iteration++)
        {
            var subscriptionReference = MaxioReference.ForSubscription(customerReference, plan.Handle, slot);

            var existing = await Guarded(
                () => _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken),
                "look up the existing subscription").ConfigureAwait(false);

            if (existing is not null)
            {
                if (SubscriptionStates.IsLive(existing.State))
                {
                    _logger.LogInformation(
                        "Subscription {Reference} already exists in state {State}; returning it unchanged.",
                        subscriptionReference, existing.State);
                    return new SubscribeResult(Map(existing, currency), created: false);
                }

                // The shopper's previous subscription to this plan has ended; start a fresh one.
                slot++;
                if (slot > MaxResubscribeSlots)
                {
                    throw new SubscriptionBillingException(
                        $"Exhausted subscription references for plan '{plan.Handle}'; the shopper has re-subscribed too many times.");
                }

                continue;
            }

            try
            {
                var created = await _client.CreateSubscriptionAsync(
                    new MaxioSubscriptionAttributes
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customer.Id,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = _settings.PaymentCollectionMethod
                    },
                    MaxioReference.NewUniquenessToken(),
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} ({Reference}) on plan {PlanHandle} in state {State}.",
                    created.Id, subscriptionReference, plan.Handle, created.State);

                return new SubscribeResult(Map(created, currency), created: true);
            }
            catch (MaxioApiException ex) when (ex.IsReferenceTaken)
            {
                // A concurrent caller claimed this reference first; loop round and adopt it.
                _logger.LogInformation(
                    "Subscription reference {Reference} was claimed concurrently; re-reading it.", subscriptionReference);
            }
            catch (MaxioApiException ex) when (ex.IsDuplicateSubmission)
            {
                // A replay of our own POST reached Maxio after the original had already been
                // accepted. If the original landed, adopt it; if it has not appeared yet, its
                // outcome is genuinely unknown and guessing would risk a double enrollment.
                var landed = await Guarded(
                    () => _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken),
                    "re-read the subscription after a duplicate submission").ConfigureAwait(false);

                if (landed is not null)
                {
                    return new SubscribeResult(Map(landed, currency), created: false);
                }

                throw new SubscriptionInProgressException(
                    "An identical subscribe request is still being processed by the billing system. Please retry in a moment.");
            }
            catch (MaxioApiException ex)
            {
                throw Translate(ex, $"subscribe to plan '{plan.Handle}'");
            }
        }

        throw new SubscriptionBillingException(
            $"Could not settle a subscription for plan '{plan.Handle}' after repeated contention.");
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> LoadPlansAsync(string familyHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var currency = await GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);
        var products = await Guarded(
            () => _client.ListProductsForFamilyAsync(familyHandle, cancellationToken),
            $"read the plan catalog for product family '{familyHandle}'").ConfigureAwait(false);

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new SubscriptionPlan(
                handle: product.Handle!,
                name: product.Name ?? product.Handle!,
                description: product.Description,
                priceInCents: product.PriceInCents,
                currency: currency,
                interval: product.Interval,
                intervalUnit: product.IntervalUnit ?? "month",
                setupFeeInCents: product.InitialChargeInCents ?? 0,
                trialInterval: product.TrialInterval,
                trialIntervalUnit: product.TrialIntervalUnit,
                trialPriceInCents: product.TrialPriceInCents,
                requiresPaymentMethod: product.RequireCreditCard,
                productFamilyHandle: product.ProductFamily?.Handle ?? familyHandle))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        string currency;
        try
        {
            var site = await _client.GetSiteAsync(cancellationToken).ConfigureAwait(false);
            currency = string.IsNullOrWhiteSpace(site?.Currency) ? "USD" : site!.Currency!;
        }
        catch (MaxioApiException ex)
        {
            // Currency is presentation detail; never fail a subscribe because the site read failed.
            _logger.LogWarning(ex, "Could not read the Maxio site currency; falling back to USD.");
            currency = "USD";
        }

        _cache.Set(SiteCacheKey, currency, _settings.CatalogCacheDuration);
        return currency;
    }

    private static SubscriptionPlan? FindPlan(IReadOnlyList<SubscriptionPlan> plans, string planHandle) =>
        plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));

    private static CustomerSubscription Map(MaxioSubscription subscription, string fallbackCurrency) =>
        new(
            id: subscription.Id,
            reference: subscription.Reference,
            state: subscription.State ?? "unknown",
            planHandle: subscription.Product?.Handle,
            planName: subscription.Product?.Name,
            planPriceInCents: subscription.ProductPriceInCents,
            currency: string.IsNullOrWhiteSpace(subscription.Currency) ? fallbackCurrency : subscription.Currency!,
            interval: subscription.Product?.Interval,
            intervalUnit: subscription.Product?.IntervalUnit,
            currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            // next_assessment_at is when a charge will actually be attempted; it tracks the period
            // end except while a failed renewal is being retried.
            nextBillingAt: subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            activatedAt: subscription.ActivatedAt,
            canceledAt: subscription.CanceledAt,
            balanceInCents: subscription.BalanceInCents,
            paymentCollectionMethod: subscription.PaymentCollectionMethod,
            customerId: subscription.Customer?.Id ?? 0,
            customerReference: subscription.Customer?.Reference);

    /// <summary>Runs a client call, translating transport and API failures into domain exceptions.</summary>
    private async Task<T> Guarded<T>(Func<Task<T>> call, string what)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, what);
        }
    }

    private SubscriptionException Translate(MaxioApiException ex, string what)
    {
        _logger.LogError(ex, "Maxio call failed while trying to {What}.", what);
        return new SubscriptionBillingException(
            $"The billing system could not {what}: {string.Join("; ", ex.Errors)}", (int)ex.StatusCode, ex);
    }

    private string RequireProductFamilyHandle()
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new SubscriptionBillingNotConfiguredException(
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}' is not configured, " +
                "so no subscription plans can be published.");
        }

        return _settings.ProductFamilyHandle!.Trim();
    }

    private void EnsureConfigured()
    {
        if (_settings.IsConfigured)
        {
            return;
        }

        throw new SubscriptionBillingNotConfiguredException(
            $"Subscription billing is unavailable: configure '{MaxioSettings.SectionName}:{nameof(MaxioSettings.ApiKey)}' " +
            $"and '{MaxioSettings.SectionName}:{nameof(MaxioSettings.Subdomain)}' (or '{MaxioSettings.SectionName}:{nameof(MaxioSettings.BaseUrl)}').");
    }
}
