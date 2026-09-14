using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
/// Implements the recurring-subscription capability on top of Maxio Advanced Billing, which is the system of
/// record: eShopOnWeb stores no plans, customers or subscriptions of its own, it projects Maxio's.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string SiteCacheKey = "maxio:site";
    private const string ProductFamilyCacheKeyPrefix = "maxio:product-family:";
    private const string PlansCacheKeyPrefix = "maxio:plans:";

    /// <summary>
    /// Collection methods (values of the specification's <c>Collection Method</c> enum) that let a
    /// subscription start without a stored payment method, keyed by the site's invoicing architecture.
    /// </summary>
    private const string RemittanceCollectionMethod = "remittance";
    private const string LegacyInvoiceCollectionMethod = "invoice";

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioOptions> options,
        IMemoryCache cache,
        KeyedAsyncLock subscribeLock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _subscribeLock = subscribeLock;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        TranslateAsync(() => ListPlansCoreAsync(cancellationToken));

    public Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default) =>
        TranslateAsync(() => SubscribeCoreAsync(subscriber, planHandle, cancellationToken));

    public Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default) =>
        TranslateAsync(() => ListSubscriptionsCoreAsync(subscriber, cancellationToken));

    private async Task<IReadOnlyList<SubscriptionPlan>> ListPlansCoreAsync(CancellationToken cancellationToken)
    {
        var options = EnsureConfigured();
        var familyHandle = options.ProductFamilyHandle!.Trim();

        if (_cache.TryGetValue(PlansCacheKeyPrefix + familyHandle, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var family = await ResolveProductFamilyAsync(familyHandle, cancellationToken).ConfigureAwait(false);
        var products = await _client.ListProductsForProductFamilyAsync(family.Id, includeArchived: false, cancellationToken)
            .ConfigureAwait(false);
        var currency = await ResolveSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

        var plans = products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => MapPlan(product, family.Handle, currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cache.Set(PlansCacheKeyPrefix + familyHandle, (IReadOnlyList<SubscriptionPlan>)plans, options.CatalogCacheDuration);
        return plans;
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A subscription plan handle is required.", nameof(planHandle));
        }

        var plan = await ResolvePlanAsync(planHandle.Trim(), cancellationToken).ConfigureAwait(false);

        // Collapse a shopper's concurrent submits so the existence check below cannot be raced in-process.
        using var _ = await _subscribeLock.AcquireAsync(subscriber.BillingReference, cancellationToken).ConfigureAwait(false);

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);
        var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
            .ConfigureAwait(false);

        var alreadySubscribed = FindLiveSubscription(existingSubscriptions, plan.Handle);
        if (alreadySubscribed is not null)
        {
            _logger.LogInformation(
                "Maxio customer {CustomerId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}, state {State}); returning the existing subscription.",
                customer.Id,
                plan.Handle,
                alreadySubscribed.Id,
                alreadySubscribed.State);

            return new SubscribeResult(MapSubscription(alreadySubscribed, customer, plan), Created: false);
        }

        var reference = BuildSubscriptionReference(customer.Reference ?? subscriber.BillingReference, plan.Handle, existingSubscriptions);

        var request = new CreateSubscription
        {
            ProductHandle = plan.Handle,
            CustomerId = customer.Id,
            Reference = reference,
            PaymentCollectionMethod = await ResolveCollectionMethodAsync(plan, cancellationToken).ConfigureAwait(false)
        };

        try
        {
            var created = await _client.CreateSubscriptionAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} (state {State}).",
                created.Id,
                customer.Id,
                plan.Handle,
                created.State);

            return new SubscribeResult(MapSubscription(created, customer, plan), Created: true);
        }
        catch (MaxioApiException ex) when (IsReferenceConflict(ex))
        {
            // Another instance enrolled this shopper on this plan concurrently; the provider rejected the
            // duplicate because the subscription reference we derive is deterministic and enforced unique.
            var replay = await ReplaySubscriptionAsync(customer, plan, reference, cancellationToken).ConfigureAwait(false);
            if (replay is not null)
            {
                return new SubscribeResult(replay, Created: false);
            }

            throw;
        }
        catch (BillingProviderUnavailableException)
        {
            // The response was lost in transit; the subscription may still have been created. The
            // deterministic reference lets us find out rather than risk enrolling the shopper twice.
            var replay = await ReplaySubscriptionAsync(customer, plan, reference, cancellationToken).ConfigureAwait(false);
            if (replay is not null)
            {
                _logger.LogWarning(
                    "Recovered subscription {SubscriptionId} for customer {CustomerId} after a failed create call.",
                    replay.Id,
                    customer.Id);

                return new SubscribeResult(replay, Created: false);
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsCoreAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        EnsureConfigured();

        var customer = await _client.ReadCustomerByReferenceAsync(subscriber.BillingReference, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            // The shopper has never subscribed, so no billing customer exists yet. That is an empty list,
            // not an error — and deliberately does not create a customer as a side effect of a read.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
            .ConfigureAwait(false);

        return subscriptions
            .Select(subscription => MapSubscription(subscription, customer, plan: null))
            .OrderByDescending(subscription => subscription.IsLive)
            .ThenByDescending(subscription => subscription.ActivatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .ToList();
    }

    // ---------------------------------------------------------------------------------------------------
    // Catalog
    // ---------------------------------------------------------------------------------------------------

    private async Task<ProductFamily> ResolveProductFamilyAsync(string handle, CancellationToken cancellationToken)
    {
        var cacheKey = ProductFamilyCacheKeyPrefix + handle;
        if (_cache.TryGetValue(cacheKey, out ProductFamily? cached) && cached is not null)
        {
            return cached;
        }

        var families = await _client.ListProductFamiliesAsync(cancellationToken).ConfigureAwait(false);
        var match = families.FirstOrDefault(family => string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var known = string.Join(", ", families.Select(family => family.Handle).Where(h => !string.IsNullOrWhiteSpace(h)));
            throw new BillingNotConfiguredException(
                $"Product family '{handle}' (from '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}') does not exist on the configured Maxio site."
                + (known.Length > 0 ? $" Available handles: {known}." : string.Empty));
        }

        _cache.Set(cacheKey, match, _options.CurrentValue.CatalogCacheDuration);
        return match;
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken).ConfigureAwait(false);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

        return plan ?? throw new SubscriptionPlanNotFoundException(planHandle);
    }

    private async Task<Site?> ResolveSiteAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCacheKey, out Site? cached))
        {
            return cached;
        }

        try
        {
            var site = await _client.ReadSiteAsync(cancellationToken).ConfigureAwait(false);
            _cache.Set(SiteCacheKey, site, _options.CurrentValue.CatalogCacheDuration);
            return site;
        }
        catch (SubscriptionBillingException ex)
        {
            // Site metadata only enriches the response (currency, invoicing architecture); never fail the
            // whole call because of it. Cache the miss briefly so we do not hammer a struggling provider.
            _logger.LogWarning(ex, "Could not read Maxio site metadata; falling back to defaults.");
            _cache.Set(SiteCacheKey, (Site?)null, TimeSpan.FromSeconds(30));
            return null;
        }
    }

    private async Task<string> ResolveSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        var site = await ResolveSiteAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(site?.Currency) ? "USD" : site!.Currency!.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Chooses how the subscription is collected. Plans that do not require a stored payment method are
    /// signed up with an invoice-style collection method so that enrolment succeeds without card capture or
    /// 3-D Secure; plans that do require a card keep the site default so the provider reports the real error.
    /// </summary>
    private async Task<string?> ResolveCollectionMethodAsync(SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        var configured = _options.CurrentValue.PaymentCollectionMethod;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!.Trim().ToLowerInvariant();
        }

        if (plan.RequiresPaymentMethod)
        {
            return null;
        }

        var site = await ResolveSiteAsync(cancellationToken).ConfigureAwait(false);
        return site is null || site.RelationshipInvoicingEnabled ? RemittanceCollectionMethod : LegacyInvoiceCollectionMethod;
    }

    // ---------------------------------------------------------------------------------------------------
    // Customer
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the Maxio customer for the shopper, creating it on first use. Idempotency rests on the
    /// customer <c>reference</c>, which Maxio enforces as unique — so a lost race is recovered by re-reading
    /// rather than by creating a second customer.
    /// </summary>
    private async Task<Customer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        var reference = subscriber.BillingReference;

        var existing = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomer
        {
            FirstName = subscriber.FirstName,
            LastName = subscriber.LastName,
            Email = subscriber.Email,
            Reference = reference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, reference);
            return created;
        }
        catch (MaxioApiException ex) when (IsReferenceConflict(ex))
        {
            var raced = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
        catch (BillingProviderUnavailableException)
        {
            var raced = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Subscription helpers
    // ---------------------------------------------------------------------------------------------------

    private static Subscription? FindLiveSubscription(IReadOnlyList<Subscription> subscriptions, string planHandle) =>
        subscriptions
            .Where(subscription =>
                SubscriptionStates.IsLive(subscription.State) &&
                string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

    /// <summary>
    /// Builds the deterministic subscription reference. It is stable for a given shopper, plan and enrolment
    /// generation, which is what turns Maxio's uniqueness constraint on <c>reference</c> into a server-side
    /// duplicate guard, while still allowing a shopper to re-subscribe after a previous subscription ended.
    /// </summary>
    internal static string BuildSubscriptionReference(
        string customerReference,
        string planHandle,
        IReadOnlyList<Subscription> existingSubscriptions)
    {
        var generation = existingSubscriptions.Count(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)) + 1;

        return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", customerReference, planHandle, generation);
    }

    private async Task<CustomerSubscription?> ReplaySubscriptionAsync(
        Customer customer,
        SubscriptionPlan plan,
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
                .ConfigureAwait(false);

            var match = subscriptions.FirstOrDefault(subscription =>
                            string.Equals(subscription.Reference, reference, StringComparison.Ordinal))
                        ?? FindLiveSubscription(subscriptions, plan.Handle);

            return match is null ? null : MapSubscription(match, customer, plan);
        }
        catch (SubscriptionBillingException ex)
        {
            _logger.LogWarning(ex, "Could not re-read subscriptions for Maxio customer {CustomerId} while recovering a failed create.", customer.Id);
            return null;
        }
    }

    private static bool IsReferenceConflict(MaxioApiException exception) =>
        exception.StatusCode == HttpStatusCode.UnprocessableEntity &&
        exception.Errors.Any(error =>
            error.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
            (error.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
             error.Contains("taken", StringComparison.OrdinalIgnoreCase)));

    // ---------------------------------------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product product, string? productFamilyHandle, string currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialIntervalLength = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? productFamilyHandle
    };

    private static CustomerSubscription MapSubscription(Subscription subscription, Customer? customer, SubscriptionPlan? plan) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        IsLive = SubscriptionStates.IsLive(subscription.State),
        PlanHandle = subscription.Product?.Handle ?? plan?.Handle,
        PlanName = subscription.Product?.Name ?? plan?.Name,
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? plan?.PriceInCents ?? 0,
        Currency = subscription.Currency ?? plan?.Currency,
        Interval = subscription.Product?.Interval ?? plan?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? plan?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        TrialEndsAt = subscription.TrialEndedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        BalanceInCents = subscription.BalanceInCents,
        BillingCustomerId = subscription.Customer?.Id ?? customer?.Id ?? 0,
        BillingCustomerReference = subscription.Customer?.Reference ?? customer?.Reference
    };

    // ---------------------------------------------------------------------------------------------------
    // Failure translation
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Keeps provider-specific failures inside this adapter: everything that escapes to callers is one of
    /// the provider-agnostic <see cref="SubscriptionBillingException"/> types the API layer knows how to map.
    /// </summary>
    private static async Task<T> TranslateAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
    }

    private static SubscriptionBillingException Translate(MaxioApiException exception) => exception.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BillingNotConfiguredException(
            $"Maxio rejected the configured credentials ({(int)exception.StatusCode}). Check '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}' and '{MaxioOptions.SectionName}:{nameof(MaxioOptions.Subdomain)}'."),

        HttpStatusCode.TooManyRequests => new BillingProviderUnavailableException(
            "Maxio is throttling requests; please retry shortly.", exception),

        _ when exception.IsClientError => new BillingRequestRejectedException(exception.Errors, exception),

        _ => new BillingProviderUnavailableException(
            $"Maxio returned an unexpected status ({(int)exception.StatusCode}).", exception)
    };

    private MaxioOptions EnsureConfigured()
    {
        var options = _options.CurrentValue;
        var failures = options.Validate();

        if (failures.Count > 0)
        {
            throw new BillingNotConfiguredException(
                "The Maxio subscription billing integration is not configured: " + string.Join(" ", failures));
        }

        return options;
    }
}
