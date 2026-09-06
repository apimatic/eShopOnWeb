using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
///
/// Maxio is the system of record: eShopOnWeb stores no plan catalog, no customer mirror and no
/// subscription rows of its own, so nothing here can drift out of sync with billing. The eShopOnWeb
/// user is bound to a Maxio customer purely through a derived, unique reference.
/// </summary>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string SiteCacheKey = "maxio:site";
    private const string PlansCacheKeyPrefix = "maxio:plans:";

    private readonly IMaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly KeyedAsyncLock _subscriberLocks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IMemoryCache cache,
        IOptionsMonitor<MaxioSettings> settings,
        KeyedAsyncLock subscriberLocks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _cache = cache;
        _settings = settings;
        _subscriberLocks = subscriberLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.CurrentValue;
        return await GetPlansAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Subscriber);

        if (string.IsNullOrWhiteSpace(request.Subscriber.Key))
        {
            throw new ArgumentException("The subscriber key is required; it is what binds the eShopOnWeb user to a billing customer.", nameof(request));
        }

        var settings = _settings.CurrentValue;
        var references = new MaxioReferenceFactory(settings.ReferencePrefix);
        var customerReference = references.CustomerReference(request.Subscriber.Key);

        // Collapse a double-click into one round trip. Correctness across instances does not depend
        // on this: it comes from the unique reference the billing system enforces below.
        using var _ = await _subscriberLocks.AcquireAsync(customerReference, cancellationToken).ConfigureAwait(false);

        return await ExecuteAsync(
            $"subscribe to '{request.PlanHandle ?? settings.DefaultPlanHandle}'",
            async () =>
            {
                var plan = await ResolvePlanAsync(settings, request.PlanHandle, cancellationToken).ConfigureAwait(false);

                if (plan.RequiresPaymentMethod)
                {
                    // Signing up would need a stored payment profile, and this integration never
                    // captures card details. Say so plainly rather than letting Maxio answer 422.
                    throw new SubscriptionNotAllowedException(
                        $"Plan '{plan.Handle}' requires a stored payment method, which this integration does not collect. " +
                        "Configure the plan with 'payment method not required', or add payment capture before subscribing to it.");
                }

                var customer = await EnsureCustomerAsync(request.Subscriber, customerReference, cancellationToken).ConfigureAwait(false);
                var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

                // 1. A replay of a request that carried an idempotency key: same key, same answer,
                //    whatever else has happened since.
                var keyed = !string.IsNullOrWhiteSpace(request.IdempotencyKey);
                var keyedReference = keyed
                    ? references.SubscriptionReference(request.Subscriber.Key, request.IdempotencyKey!)
                    : null;

                if (keyedReference is not null)
                {
                    var replay = existing.FirstOrDefault(s => string.Equals(s.Reference, keyedReference, StringComparison.Ordinal))
                        ?? await _client.FindSubscriptionByReferenceAsync(keyedReference, cancellationToken).ConfigureAwait(false);

                    if (replay is not null)
                    {
                        _logger.LogInformation(
                            "Subscribe replayed for customer {CustomerId} with idempotency key; returning existing subscription {SubscriptionId}.",
                            customer.Id,
                            replay.Id);
                        return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, MapSubscription(replay, customerReference));
                    }
                }

                // 2. A live subscription to the same plan is a duplicate regardless of how the
                //    request was keyed: one shopper does not hold the same plan twice.
                var live = existing.FirstOrDefault(s =>
                    SubscriptionStates.IsLive(s.State)
                    && string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));

                if (live is not null)
                {
                    _logger.LogInformation(
                        "Customer {CustomerId} already holds subscription {SubscriptionId} to plan '{PlanHandle}' in state '{State}'; not creating a duplicate.",
                        customer.Id,
                        live.Id,
                        plan.Handle,
                        live.State);
                    return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, MapSubscription(live, customerReference));
                }

                // 3. Nothing to adopt, so create. Without a caller-supplied key the reference is
                //    derived from state both racers can see - the number of subscriptions this
                //    customer already has on the plan - so concurrent requests collide on the
                //    billing system's uniqueness check instead of each creating a subscription,
                //    while a shopper re-subscribing after a cancellation moves to the next number.
                var reference = keyedReference ?? references.SubscriptionReference(
                    request.Subscriber.Key,
                    plan.Handle,
                    existing.Count(s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)) + 1);

                return await CreateAsync(request, plan, customer, customerReference, reference, settings, cancellationToken).ConfigureAwait(false);
            });
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var settings = _settings.CurrentValue;
        var customerReference = new MaxioReferenceFactory(settings.ReferencePrefix).CustomerReference(subscriber.Key);

        return await ExecuteAsync("list subscriptions", async () =>
        {
            var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
            if (customer is null)
            {
                // Never enrolled. An empty list is the answer, not an error.
                return (IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

            return subscriptions
                .Select(s => MapSubscription(s, customerReference))
                .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(s => s.Id)
                .ToList();
        });
    }

    private async Task<SubscribeResult> CreateAsync(
        SubscribeRequest request,
        SubscriptionPlan plan,
        MaxioCustomer customer,
        string customerReference,
        string subscriptionReference,
        MaxioSettings settings,
        CancellationToken cancellationToken)
    {
        var collectionMethod = await ResolveCollectionMethodAsync(settings, cancellationToken).ConfigureAwait(false);

        try
        {
            var created = await _client.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = collectionMethod,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({Reference}) for customer {CustomerId} on plan '{PlanHandle}'; state {State}, next billing {NextBillingAt:o}.",
                created.Id,
                created.Reference,
                customer.Id,
                plan.Handle,
                created.State,
                created.NextAssessmentAt ?? created.CurrentPeriodEndsAt);

            return new SubscribeResult(SubscribeOutcome.Created, MapSubscription(created, customerReference));
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict)
        {
            // Another request already claimed this reference. That is exactly the duplicate we
            // wanted the billing system to reject; adopt the winner rather than failing the caller.
            var winner = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken).ConfigureAwait(false);
            if (winner is null)
            {
                throw;
            }

            _logger.LogInformation(
                "Concurrent subscribe for customer {CustomerId} on plan '{PlanHandle}' lost the reference race; returning subscription {SubscriptionId}.",
                customer.Id,
                plan.Handle,
                winner.Id);

            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, MapSubscription(winner, customerReference));
        }
    }

    /// <summary>
    /// Returns the billing customer for this user, creating it on first subscribe. The lookup is
    /// by reference, and a lost creation race is recovered by re-reading, so this can never leave
    /// two customers behind for one eShopOnWeb user.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(Subscriber subscriber, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);

        try
        {
            var created = await _client.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    Reference = customerReference,
                    Email = subscriber.Email,
                    FirstName = firstName,
                    LastName = lastName,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, customerReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsReferenceConflict)
        {
            var winner = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken).ConfigureAwait(false);
            if (winner is null)
            {
                throw;
            }

            _logger.LogInformation("Adopted concurrently created Maxio customer {CustomerId} for reference {CustomerReference}.", winner.Id, customerReference);
            return winner;
        }
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(MaxioSettings settings, string? requestedHandle, CancellationToken cancellationToken)
    {
        var handle = string.IsNullOrWhiteSpace(requestedHandle) ? settings.DefaultPlanHandle : requestedHandle.Trim();
        var plans = await GetPlansAsync(settings, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new SubscriptionPlanNotFoundException(null, plans.Select(p => p.Handle));
        }

        return plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(handle, plans.Select(p => p.Handle));
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(MaxioSettings settings, CancellationToken cancellationToken)
    {
        var cacheKey = PlansCacheKeyPrefix + settings.ProductFamilyHandle;
        if (settings.CatalogCacheDuration > TimeSpan.Zero
            && _cache.TryGetValue<IReadOnlyList<SubscriptionPlan>>(cacheKey, out var cached)
            && cached is not null)
        {
            return cached;
        }

        var plans = await ExecuteAsync("list subscription plans", async () =>
        {
            var site = await GetSiteAsync(settings, cancellationToken).ConfigureAwait(false);

            var family = await _client.FindProductFamilyByHandleAsync(settings.ProductFamilyHandle, cancellationToken).ConfigureAwait(false);
            if (family is null)
            {
                throw new BillingProviderException(
                    $"Maxio product family '{settings.ProductFamilyHandle}' was not found on site '{site.Subdomain ?? settings.Subdomain}'. " +
                    "Check Maxio:ProductFamilyHandle.");
            }

            var products = await _client.ListProductsForFamilyAsync(family.Id, cancellationToken).ConfigureAwait(false);

            return (IReadOnlyList<SubscriptionPlan>)products
                .Where(p => !string.IsNullOrWhiteSpace(p.Handle))
                .Select(p => MapPlan(p, family, site.Currency))
                .OrderBy(p => p.PriceInCents)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }).ConfigureAwait(false);

        if (settings.CatalogCacheDuration > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, plans, settings.CatalogCacheDuration);
        }

        return plans;
    }

    private async Task<MaxioSite> GetSiteAsync(MaxioSettings settings, CancellationToken cancellationToken)
    {
        if (settings.CatalogCacheDuration > TimeSpan.Zero
            && _cache.TryGetValue<MaxioSite>(SiteCacheKey, out var cached)
            && cached is not null)
        {
            return cached;
        }

        var site = await _client.GetSiteAsync(cancellationToken).ConfigureAwait(false);

        if (settings.CatalogCacheDuration > TimeSpan.Zero)
        {
            _cache.Set(SiteCacheKey, site, settings.CatalogCacheDuration);
        }

        return site;
    }

    /// <summary>
    /// Picks the payment collection method for a signup.
    ///
    /// The default "automatic" would try to capture the first invoice immediately and fail with
    /// "no payment method was on file" for any priced plan, because this integration stores no
    /// payment profile. Invoicing the shopper instead lets the subscription activate. The valid
    /// value depends on the site's invoicing architecture, so it is read from the site rather than
    /// assumed - and can be overridden outright by configuration.
    /// </summary>
    private async Task<string> ResolveCollectionMethodAsync(MaxioSettings settings, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.PaymentCollectionMethod))
        {
            return settings.PaymentCollectionMethod.Trim();
        }

        var site = await GetSiteAsync(settings, cancellationToken).ConfigureAwait(false);
        return site.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, MaxioProductFamily family, string? currency) => new()
    {
        Handle = product.Handle!,
        Id = product.Id,
        Name = product.Name ?? product.Handle!,
        Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency ?? string.Empty,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        SetupFeeInCents = product.InitialChargeInCents,
        RequiresPaymentMethod = product.RequireCreditCard == true,
        ProductFamilyHandle = family.Handle ?? string.Empty,
        ProductFamilyName = family.Name,
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, string customerReference) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PlanId = subscription.Product?.Id,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency ?? string.Empty,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        // next_assessment_at is the collection attempt; it only diverges from the period end while
        // a failed payment is being retried, and in that case it is the date that matters.
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference ?? customerReference,
    };

    private static (string FirstName, string LastName) ResolveName(Subscriber subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(first) || !string.IsNullOrEmpty(last))
        {
            return (first ?? string.Empty, last ?? string.Empty);
        }

        // eShopOnWeb accounts carry no name, so fall back to the local part of the email. Maxio
        // stores this on the customer record only; nothing downstream keys off it.
        var email = subscriber.Email ?? string.Empty;
        var at = email.IndexOf('@');
        var localPart = at > 0 ? email[..at] : email;

        return (string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart, "Subscriber");
    }

    /// <summary>
    /// Translates provider-level failures into the application's billing exception, so callers
    /// never have to know the integration speaks HTTP. Application-level exceptions raised inside
    /// the operation pass through untouched.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(string operation, Func<Task<T>> operationBody)
    {
        try
        {
            return await operationBody().ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            var summary = ex.IsAuthFailure
                ? $"Maxio Advanced Billing rejected the credentials while trying to {operation}. Check Maxio:ApiKey and Maxio:Subdomain."
                : $"Maxio Advanced Billing could not {operation}.";

            _logger.LogError(ex, "Maxio operation failed: {Operation} ({StatusCode}).", operation, ex.StatusCode);

            throw new BillingProviderException(
                summary,
                ex.StatusCode is null ? null : (int)ex.StatusCode,
                ex.Errors,
                ex.IsTransient,
                ex);
        }
    }
}
