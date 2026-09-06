using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing as the system of record.
/// <para>
/// eShopOnWeb stores no subscription state of its own. Every shopper maps onto a Maxio customer
/// through a deterministic <c>reference</c> derived from their user name, and every enrollment maps
/// onto a Maxio subscription through a deterministic reference derived from the shopper and the plan.
/// Because Maxio enforces those references as unique per site, enrollment stays idempotent across
/// double-clicks, retries, process restarts and multiple application instances - without a local
/// mapping table that an in-memory database would lose on restart.
/// </para>
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// How many reference slots to walk when a shopper re-subscribes to a plan they previously held.
    /// Each ended subscription permanently consumes one reference, so the next enrollment takes the
    /// next slot.
    /// </summary>
    private const int MaxReferenceAttempts = 25;

    private readonly IMaxioApiClient _client;
    private readonly MaxioSiteCache _siteCache;
    private readonly SubscriberLocks _subscriberLocks;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        MaxioSiteCache siteCache,
        SubscriberLocks subscriberLocks,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _siteCache = siteCache;
        _subscriberLocks = subscriberLocks;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var site = await GetSiteAsync(cancellationToken);
        var products = await CallAsync(
            ct => _client.ListProductsForProductFamilyAsync(_options.ProductFamilyHandle!, ct),
            "load the subscription plans",
            cancellationToken);

        return products
            // Archived products are still returned by Maxio; they are no longer sellable.
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p, site))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        EnsureConfigured();

        var site = await GetSiteAsync(cancellationToken);

        var customer = await CallAsync(
            ct => _client.ReadCustomerByReferenceAsync(subscriber.CustomerReference, ct),
            "load your billing account",
            cancellationToken);

        if (customer is null)
        {
            // A shopper who has never subscribed has no Maxio customer, which is not an error.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await CallAsync(
            ct => _client.ListCustomerSubscriptionsAsync(customer.Id, ct),
            "load your subscriptions",
            cancellationToken);

        return subscriptions
            .Select(s => MapSubscription(s, site))
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(
        Subscriber subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        EnsureConfigured();

        var site = await GetSiteAsync(cancellationToken);

        // Only products in the configured family are sellable here, so resolve the requested handle
        // against that family rather than trusting whatever the caller sent.
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw new SubscriptionPlanNotFoundException(planHandle);

        using var _ = await _subscriberLocks.AcquireAsync(subscriber.CustomerReference, cancellationToken);

        var customer = await EnsureCustomerAsync(subscriber, cancellationToken);

        var existing = await CallAsync(
            ct => _client.ListCustomerSubscriptionsAsync(customer.Id, ct),
            "check your existing subscriptions",
            cancellationToken);

        var alreadySubscribed = existing.FirstOrDefault(s => MatchesPlan(s, plan.Handle) && SubscriptionStates.IsEngaged(s.State));
        if (alreadySubscribed is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerReference} is already subscribed to {PlanHandle} (subscription {SubscriptionId}); returning the existing subscription.",
                subscriber.CustomerReference, plan.Handle, alreadySubscribed.Id);

            return new SubscribeResult(MapSubscription(alreadySubscribed, site), created: false);
        }

        var collectionMethod = await ResolveCollectionMethodAsync(cancellationToken);

        for (var attempt = 1; attempt <= MaxReferenceAttempts; attempt++)
        {
            var reference = subscriber.SubscriptionReference(plan.Handle, attempt);

            var taken = await CallAsync(
                ct => _client.FindSubscriptionAsync(reference, ct),
                "check your existing subscriptions",
                cancellationToken);

            if (taken is not null)
            {
                if (MatchesPlan(taken, plan.Handle) && SubscriptionStates.IsEngaged(taken.State))
                {
                    // Another instance enrolled this shopper while we were working.
                    return new SubscribeResult(MapSubscription(taken, site), created: false);
                }

                // The reference belongs to an ended subscription; move to the next slot.
                continue;
            }

            var request = new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = reference,
                PaymentCollectionMethod = collectionMethod
            };

            try
            {
                var created = await _client.CreateSubscriptionAsync(request, cancellationToken);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} ({Reference}) on plan {PlanHandle} for customer {CustomerId}.",
                    created.Id, created.Reference, plan.Handle, customer.Id);

                return new SubscribeResult(MapSubscription(created, site), created: true);
            }
            catch (MaxioApiException ex) when (ex.IsDuplicateReference)
            {
                // Lost a race for this reference. Re-read it: if it is now this shopper's live
                // subscription the enrollment already happened, otherwise take the next slot.
                _logger.LogInformation(
                    "Subscription reference {Reference} was taken concurrently; re-resolving.", reference);

                var raced = await CallAsync(
                    ct => _client.FindSubscriptionAsync(reference, ct),
                    "confirm your subscription",
                    cancellationToken);

                if (raced is not null && MatchesPlan(raced, plan.Handle) && SubscriptionStates.IsEngaged(raced.State))
                {
                    return new SubscribeResult(MapSubscription(raced, site), created: false);
                }
            }
            catch (MaxioApiException ex)
            {
                _logger.LogError(ex, "Maxio rejected the subscription to {PlanHandle} for customer {CustomerId}.",
                    plan.Handle, customer.Id);

                throw new BillingProviderException(
                    $"The billing provider could not start a subscription to '{plan.Handle}'.", ex.Errors, ex);
            }
        }

        throw new BillingProviderException(
            $"Could not find a free subscription reference for '{plan.Handle}' after {MaxReferenceAttempts} attempts.");
    }

    /// <summary>
    /// Resolves the shopper's Maxio customer, creating it on first use. Safe to repeat: the customer
    /// reference is unique per site, so a lost creation race resolves to the winner's record.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await CallAsync(
            ct => _client.ReadCustomerByReferenceAsync(subscriber.CustomerReference, ct),
            "load your billing account",
            cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioCreateCustomer
        {
            FirstName = subscriber.FirstName,
            LastName = subscriber.LastName,
            Email = subscriber.Email,
            Reference = subscriber.CustomerReference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id, created.Reference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            var raced = await CallAsync(
                ct => _client.ReadCustomerByReferenceAsync(subscriber.CustomerReference, ct),
                "load your billing account",
                cancellationToken);

            if (raced is not null)
            {
                return raced;
            }

            throw new BillingProviderException(
                "The billing provider reported an existing account that could not be read back.", ex.Errors, ex);
        }
        catch (MaxioApiException ex)
        {
            throw new BillingProviderException(
                "The billing provider could not create your billing account.", ex.Errors, ex);
        }
    }

    /// <summary>
    /// Chooses the <c>payment_collection_method</c> to enroll with.
    /// <para>
    /// eShopOnWeb never captures card data, so subscriptions are created on an invoice-style
    /// collection method; <c>automatic</c> would require a stored payment profile and Maxio would
    /// refuse the signup. The specification's Collection Method schema ties the valid values to the
    /// site's billing architecture, which the site record reports.
    /// </para>
    /// </summary>
    private async Task<string> ResolveCollectionMethodAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.PaymentCollectionMethod))
        {
            return _options.PaymentCollectionMethod!.Trim().ToLowerInvariant();
        }

        var site = await GetSiteAsync(cancellationToken);
        return site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
    }

    private Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken) =>
        _siteCache.GetAsync(
            ct => CallAsync(c => _client.ReadSiteAsync(c), "load the billing site settings", ct),
            cancellationToken);

    /// <summary>
    /// Runs a Maxio call and converts a provider-level failure into an application-level one, so
    /// callers never have to know which billing provider is behind the interface.
    /// </summary>
    private async Task<T> CallAsync<T>(
        Func<CancellationToken, Task<T>> call,
        string description,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio call failed while trying to {Description}.", description);
            throw new BillingProviderException(
                $"The billing provider could not {description}.", ex.Errors, ex);
        }
    }

    private void EnsureConfigured()
    {
        var failures = _options.Validate();
        if (failures.Count == 0)
        {
            return;
        }

        throw new BillingNotConfiguredException(
            "Subscription billing is not configured. " + string.Join(" ", failures));
    }

    private static bool MatchesPlan(MaxioSubscription subscription, string planHandle) =>
        string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase);

    private static SubscriptionPlan MapPlan(MaxioProduct product, MaxioSite site) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = site.Currency ?? string.Empty,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        HasTrial = product.TrialInterval is > 0,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        InitialChargeInCents = product.InitialChargeInCents ?? 0,
        RequiresPaymentMethod = product.RequireCreditCard,
        PricePointName = product.ProductPricePointName
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, MaxioSite site) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency ?? site.Currency ?? string.Empty,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference
    };
}
