using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Recurring subscription billing backed by Maxio Advanced Billing as the system of record.
/// </summary>
/// <remarks>
/// eShopOnWeb stores no subscription state of its own: plans, customers and subscriptions are read
/// from and written to Advanced Billing on every call, keyed by deterministic references derived from
/// the eShopOnWeb user. That keeps the two systems from drifting and survives a host restart even
/// when eShopOnWeb runs on the in-memory database.
/// </remarks>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// How many reference slots to try before giving up. A slot is skipped only when it is held by a
    /// finished subscription, so this bounds how often the same shopper may re-subscribe to one plan.
    /// </summary>
    private const int MaxReferenceAttempts = 25;

    private const string PlansCacheKeyPrefix = "maxio:plans:";
    private const string SiteCacheKey = "maxio:site";

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _enrollmentLocks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        KeyedAsyncLock enrollmentLocks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _enrollmentLocks = enrollmentLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currency = await GetSiteCurrencyAsync(cancellationToken);
            var products = await GetProductsAsync(cancellationToken);

            return products
                .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
                .Select(p => MapPlan(p, currency))
                .OrderBy(p => p.PriceInCents)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(
        Subscriber subscriber,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        var plan = await FindPlanAsync(planHandle, cancellationToken)
                   ?? throw new SubscriptionPlanNotFoundException(planHandle);

        if (plan.RequiresPaymentMethod)
        {
            // Enrolling in such a plan needs a stored card, which means Maxio.js / 3-D Secure card
            // capture. That is out of scope here, so fail loudly instead of letting Maxio reject the
            // signup with a less obvious message.
            throw new SubscriptionNotAllowedException(
                $"Plan '{plan.Handle}' requires a stored payment method, which this integration does not capture.");
        }

        var customerReference = MaxioReference.ForCustomer(_options.ReferencePrefix, subscriber.UserKey);

        // Collapse a double click into one round trip. The provider's unique-reference constraint,
        // handled below, is what makes the operation idempotent across processes.
        using var _ = await _enrollmentLocks.AcquireAsync(customerReference, cancellationToken);

        try
        {
            var customer = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken);

            var existing = await _client.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
            var liveOnPlan = existing.FirstOrDefault(s => IsLiveSubscriptionForPlan(s, plan.Handle));
            if (liveOnPlan is not null)
            {
                _logger.LogInformation(
                    "Customer {CustomerReference} is already subscribed to {PlanHandle} (subscription {SubscriptionId}); returning the existing subscription.",
                    customerReference,
                    plan.Handle,
                    liveOnPlan.Id);

                return new SubscriptionEnrollment(MapSubscription(liveOnPlan), AlreadyExisted: true);
            }

            return await CreateSubscriptionAsync(customerReference, plan, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        var customerReference = MaxioReference.ForCustomer(_options.ReferencePrefix, subscriber.UserKey);

        try
        {
            var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                // The shopper has never subscribed, so no billing customer exists yet.
                return Array.Empty<Subscription>();
            }

            var subscriptions = await _client.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);

            return subscriptions
                .Select(MapSubscription)
                .OrderByDescending(s => s.ActivatedAt ?? s.CurrentPeriodStartsAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(s => s.Id)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
    }

    /// <summary>
    /// Returns the Advanced Billing customer for the shopper, creating it on first use.
    /// </summary>
    /// <remarks>
    /// Look up first, then create. If a concurrent caller created it in between, the provider rejects
    /// the second create with a duplicate-reference error and the record is read back instead.
    /// </remarks>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        Subscriber subscriber,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
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
                    FirstName = firstName,
                    LastName = lastName,
                    Email = subscriber.Email,
                    Reference = customerReference
                },
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id,
                customerReference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            _logger.LogInformation(
                "Maxio customer {CustomerReference} was created concurrently; reusing the existing record.",
                customerReference);

            return await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken)
                   ?? throw new SubscriptionBillingUnavailableException(
                       $"Maxio reported customer reference '{customerReference}' as taken but did not return the customer.");
        }
    }

    /// <summary>
    /// Creates the subscription, walking to the next reference slot when the natural one is already
    /// held by a finished subscription from an earlier signup.
    /// </summary>
    private async Task<SubscriptionEnrollment> CreateSubscriptionAsync(
        string customerReference,
        SubscriptionPlan plan,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxReferenceAttempts; attempt++)
        {
            var reference = MaxioReference.ForSubscription(customerReference, plan.Handle, attempt);

            try
            {
                var created = await _client.CreateSubscriptionAsync(
                    new MaxioCreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerReference = customerReference,
                        Reference = reference,
                        PaymentCollectionMethod = NormalizeCollectionMethod(_options.PaymentCollectionMethod)
                    },
                    cancellationToken);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} ({Reference}) on plan {PlanHandle} for customer {CustomerReference}.",
                    created.Id,
                    reference,
                    plan.Handle,
                    customerReference);

                return new SubscriptionEnrollment(MapSubscription(created), AlreadyExisted: false);
            }
            catch (MaxioApiException ex) when (ex.IsDuplicateReference)
            {
                var occupant = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken);

                if (occupant is not null && SubscriptionStates.IsLive(occupant.State))
                {
                    // Either a concurrent request won the race, or an earlier signup is still running.
                    _logger.LogInformation(
                        "Subscription reference {Reference} is already held by live subscription {SubscriptionId}; returning it.",
                        reference,
                        occupant.Id);

                    return new SubscriptionEnrollment(MapSubscription(occupant), AlreadyExisted: true);
                }

                // The slot belongs to a finished subscription: this shopper is re-subscribing, so
                // move on to the next slot.
                _logger.LogInformation(
                    "Subscription reference {Reference} is held by a finished subscription; trying the next reference.",
                    reference);
            }
        }

        throw new SubscriptionBillingException(
            $"Could not allocate a free subscription reference for '{customerReference}' on plan '{plan.Handle}' after {MaxReferenceAttempts} attempts.");
    }

    private async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        var cacheKey = PlansCacheKeyPrefix + _options.ProductFamilyHandle;

        if (_options.PlanCacheSeconds > 0 &&
            _cache.TryGetValue(cacheKey, out IReadOnlyList<MaxioProduct>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        if (_options.PlanCacheSeconds > 0)
        {
            _cache.Set(cacheKey, products, TimeSpan.FromSeconds(_options.PlanCacheSeconds));
        }

        return products;
    }

    /// <summary>
    /// Products do not carry a currency; the site's currency is the one its default price points bill in.
    /// </summary>
    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        var site = await _client.GetSiteAsync(cancellationToken);
        var currency = string.IsNullOrWhiteSpace(site.Currency) ? "USD" : site.Currency;

        // The site currency effectively never changes, so cache it for the life of the process.
        _cache.Set(SiteCacheKey, currency, TimeSpan.FromHours(12));

        return currency;
    }

    private static bool IsLiveSubscriptionForPlan(MaxioSubscription subscription, string planHandle) =>
        SubscriptionStates.IsLive(subscription.State) &&
        string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase);

    private static SubscriptionPlan MapPlan(MaxioProduct product, string currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialPriceInCents = product.TrialPriceInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit
    };

    private static Subscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency ?? "USD",
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,

        // Advanced Billing exposes the next billing moment as next_assessment_at; the period end is
        // the same instant for a subscription that renews normally, and is the sensible fallback.
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt ?? subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference
    };

    private static (string FirstName, string LastName) ResolveName(Subscriber subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(first) || !string.IsNullOrEmpty(last))
        {
            return (first ?? string.Empty, last ?? string.Empty);
        }

        // eShopOnWeb identities carry no name, so fall back to the local part of the email address.
        var localPart = subscriber.Email.Split('@')[0];
        return (string.IsNullOrWhiteSpace(localPart) ? subscriber.Email : localPart, "eShopOnWeb");
    }

    private static string? NormalizeCollectionMethod(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? null : configured.Trim().ToLowerInvariant();

    /// <summary>
    /// Turns a provider failure into the vocabulary the application layer understands.
    /// </summary>
    private SubscriptionBillingException Translate(MaxioApiException ex)
    {
        if (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Maxio rejected the API credentials. Check the Maxio:ApiKey and Maxio:Subdomain settings.");
            return new SubscriptionBillingUnavailableException(
                "The billing provider rejected this application's credentials.", ex);
        }

        if (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new SubscriptionBillingUnavailableException(
                $"The billing provider does not expose {ex.Method} {ex.Path}. Check the Maxio:ProductFamilyHandle and Maxio:BaseUrl settings.",
                ex);
        }

        if (ex.IsValidationFailure)
        {
            return new SubscriptionNotAllowedException(
                ex.Errors.Count > 0
                    ? string.Join(" ", ex.Errors)
                    : "The billing provider rejected the request.");
        }

        return new SubscriptionBillingUnavailableException(
            "The billing provider could not complete the request.", ex);
    }
}
