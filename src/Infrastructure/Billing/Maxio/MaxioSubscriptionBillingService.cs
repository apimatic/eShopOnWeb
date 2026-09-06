using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// <para>
/// Maxio is the system of record: nothing about customers or subscriptions is stored locally. The
/// link back to an eShopOnWeb shopper is the Maxio customer <c>reference</c>, derived
/// deterministically from the authenticated user name, which is what lets "my subscriptions" work
/// after a restart even on the in-memory database.
/// </para>
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlanCacheKeyPrefix = "maxio:plans:";
    private const string SiteCacheKey = "maxio:site";
    private static readonly TimeSpan SiteCacheDuration = TimeSpan.FromMinutes(30);

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscriberLocks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(IMaxioApiClient client, IOptions<MaxioOptions> options,
        IMemoryCache cache, KeyedAsyncLock subscriberLocks, ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _subscriberLocks = subscriberLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = PlanCacheKeyPrefix + _options.ProductFamilyHandle;

        if (_options.CatalogCacheSeconds > 0
            && _cache.TryGetValue<IReadOnlyList<SubscriptionPlan>>(cacheKey, out var cached)
            && cached is not null)
        {
            return cached;
        }

        var site = await GetSiteInfoAsync(cancellationToken);
        var currency = site.Currency;
        var products = await _client.ListProductsForFamilyAsync(_options.ProductFamilyHandle, cancellationToken);

        var plans = products
            // Archived products are still returned when explicitly requested; they are never offerable.
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => MapPlan(product, currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_options.CatalogCacheSeconds > 0)
        {
            _cache.Set<IReadOnlyList<SubscriptionPlan>>(cacheKey, plans,
                TimeSpan.FromSeconds(_options.CatalogCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await FindPlanAsync(request.PlanHandle, cancellationToken)
                   ?? throw new PlanNotFoundException(request.PlanHandle, _options.ProductFamilyHandle);

        var reference = MaxioCustomerReference.For(request.Subscriber.UserName, _options.CustomerReferencePrefix);

        // Serialize this shopper's subscribe attempts so the "already subscribed?" check below cannot
        // be raced by a second click that is about to create a duplicate.
        using var _ = await _subscriberLocks.AcquireAsync(reference, cancellationToken);

        var customer = await EnsureCustomerAsync(request.Subscriber, reference, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, plan.Currency, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}, state {State}); returning the existing subscription.",
                customer.Id, plan.Handle, existing.Id, existing.State);
            return new SubscribeResult(existing, plan, alreadySubscribed: true);
        }

        var site = await GetSiteInfoAsync(cancellationToken);
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = PaymentCollectionMethodFor(plan, site)
            },
            UniquenessToken = BuildUniquenessToken("subscribe", reference, plan.Handle, request.IdempotencyKey)
        };

        MaxioSubscription created;
        try
        {
            created = await _client.CreateSubscriptionAsync(payload, cancellationToken);
        }
        catch (BillingConflictException ex)
        {
            // Maxio saw an identical submission inside its de-duplication window. Either the earlier
            // one succeeded - in which case the shopper now has a live subscription we should return -
            // or it did not, and this is a genuine new signup that merely reused a derived token.
            _logger.LogInformation(ex,
                "Maxio flagged the subscribe for customer {CustomerId} on plan {PlanHandle} as a duplicate submission; reconciling.",
                customer.Id, plan.Handle);

            var reconciled = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, plan.Currency, cancellationToken);
            if (reconciled is not null)
            {
                return new SubscribeResult(reconciled, plan, alreadySubscribed: true);
            }

            if (request.IdempotencyKey is not null)
            {
                // The caller asked for strict idempotency under their own key; do not quietly retry
                // under a different one - tell them so they can re-read and decide.
                throw new BillingConflictException(
                    $"A subscribe request with idempotency key '{request.IdempotencyKey}' was already submitted to Maxio but produced no live subscription. Re-read /api/my-subscriptions before retrying.");
            }

            payload.UniquenessToken = BuildUniquenessToken("subscribe", reference, plan.Handle,
                Guid.NewGuid().ToString("n"));
            created = await _client.CreateSubscriptionAsync(payload, cancellationToken);
        }

        var subscription = MapSubscription(created, plan.Currency);
        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} (state {State}).",
            subscription.Id, customer.Id, plan.Handle, subscription.State);

        return new SubscribeResult(subscription, plan, alreadySubscribed: false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var reference = MaxioCustomerReference.For(subscriber.UserName, _options.CustomerReferencePrefix);
        var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);

        if (customer is null)
        {
            // The shopper has never subscribed. That is a normal, empty answer - not an error, and
            // deliberately not a reason to create a billing customer.
            return Array.Empty<CustomerSubscription>();
        }

        var site = await GetSiteInfoAsync(cancellationToken);
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(subscription => MapSubscription(subscription, site.Currency))
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Returns the Maxio customer for this shopper, creating it if it does not exist yet. Safe to run
    /// concurrently: the reference is unique in Maxio, so a losing race is detected and resolved by
    /// re-reading rather than by creating a second customer.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, string reference,
        CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName, organization) = MaxioCustomerName.Derive(subscriber);
        var payload = new CreateCustomerRequest
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Organization = organization,
                Reference = reference
            },
            UniquenessToken = BuildUniquenessToken("customer", reference, string.Empty, null)
        };

        try
        {
            var created = await _client.CreateCustomerAsync(payload, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.",
                created.Id, reference);
            return created;
        }
        catch (Exception ex) when (ex is BillingValidationException or BillingConflictException)
        {
            // Someone else created it between our lookup and our write - Maxio enforces one customer
            // per reference, so the correct recovery is to read theirs, not to try again.
            var raced = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerId} for reference {Reference} already existed; using it.",
                    raced.Id, reference);
                return raced;
            }

            throw;
        }
    }

    private async Task<CustomerSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle,
        string? currency, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Where(subscription => SubscriptionStates.IsLive(subscription.State)
                                   && string.Equals(subscription.Product?.Handle, planHandle,
                                       StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(subscription => MapSubscription(subscription, currency))
            .FirstOrDefault();
    }

    private async Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetPlansAsync(cancellationToken);
        return plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads the site once and caches it: its currency labels every amount we report, and its billing
    /// architecture decides which non-automatic collection method a signup may ask for. A failure here
    /// must degrade, not take the catalog down with it.
    /// </summary>
    private async Task<SiteInfo> GetSiteInfoAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<SiteInfo>(SiteCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var info = SiteInfo.Unknown;
        try
        {
            var site = await _client.ReadSiteAsync(cancellationToken);
            if (site is not null)
            {
                info = new SiteInfo(site.Currency, site.RelationshipInvoicingEnabled);
            }
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex,
                "Could not read the Maxio site settings; falling back to defaults for currency and collection method.");
        }

        _cache.Set(SiteCacheKey, info, SiteCacheDuration);
        return info;
    }

    /// <summary>
    /// Chooses how the signup collects payment. These plans do not require a payment method, and this
    /// integration deliberately captures no card details, so the subscription is invoiced rather than
    /// auto-charged - otherwise Maxio would try to settle the first period against a card that is not
    /// there. The valid non-automatic value depends on the site's billing architecture: Relationship
    /// Invoicing sites take <c>remittance</c>, legacy Statements sites take <c>invoice</c>.
    /// </summary>
    private string PaymentCollectionMethodFor(SubscriptionPlan plan, SiteInfo site)
    {
        if (!string.IsNullOrWhiteSpace(_options.PaymentCollectionMethod))
        {
            return _options.PaymentCollectionMethod!.Trim();
        }

        if (plan.RequiresPaymentMethod)
        {
            // The plan insists on a stored payment method, which this integration has no way to
            // capture. Say so plainly instead of letting Maxio fail the charge.
            throw new BillingValidationException(
                $"Plan '{plan.Handle}' requires a payment method on file. This integration does not capture card details, so it can only subscribe to plans configured without that requirement.");
        }

        return site.RelationshipInvoicing ? "remittance" : "invoice";
    }

    /// <summary>Site-level facts that shape every request, read once and cached.</summary>
    private sealed record SiteInfo(string? Currency, bool RelationshipInvoicing)
    {
        public static readonly SiteInfo Unknown = new(null, RelationshipInvoicing: true);
    }

    /// <summary>
    /// Builds the <c>uniqueness_token</c> sent with a write. Maxio rejects a repeat of the same token
    /// within an hour with 409, which is exactly the guard we want against a double-submitted signup.
    /// Without a caller-supplied key the token is derived from (customer, plan), so two clicks on the
    /// same button collide even when they land on different instances.
    /// </summary>
    private string BuildUniquenessToken(string scope, string reference, string planHandle, string? idempotencyKey)
    {
        var material = string.Join('|', _options.CustomerReferencePrefix, scope, reference, planHandle,
            idempotencyKey ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));

        var builder = new StringBuilder(scope.Length + 33);
        builder.Append(scope).Append('-');
        for (var i = 0; i < 16; i++)
        {
            builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private SubscriptionPlan MapPlan(MaxioProduct product, string? currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency ?? string.Empty,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard,
        HasTrial = product.TrialInterval is > 0,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        PricePointHandle = product.ProductPricePointHandle,
        PricePointName = product.ProductPricePointName,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? _options.ProductFamilyHandle,
        ProviderPlanId = product.Id
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, string? currency) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        // product_price_in_cents is what this subscription is actually billed, which can differ from
        // the catalog price if the plan was re-priced after signup.
        PriceInCents = subscription.ProductPriceInCents,
        Currency = currency ?? string.Empty,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        BalanceInCents = subscription.BalanceInCents,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        CreatedAt = subscription.CreatedAt ?? DateTimeOffset.MinValue
    };
}
