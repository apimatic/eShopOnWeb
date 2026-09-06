using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing, which is the system of record: this class
/// keeps no local copy of who is subscribed to what, it asks Maxio every time.
/// </summary>
/// <remarks>
/// Subscribing is idempotent on three levels, because any one of them can be defeated on its own:
/// <list type="number">
/// <item>the Maxio customer is keyed by a reference derived from the eShopOnWeb account, and Maxio
/// allows only one customer per reference;</item>
/// <item>an existing non-terminal subscription to the requested plan is returned as-is instead of a
/// second one being created;</item>
/// <item>writes carry a uniqueness token so a racing or replayed request is rejected by Maxio's
/// duplicate prevention rather than performed twice, and the rejection is resolved by re-reading.</item>
/// </list>
/// </remarks>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlansCacheKeyPrefix = "maxio:plans:";
    private const string SiteCacheKey = "maxio:site";

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscribeLocks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        MaxioSettings settings,
        IMemoryCache cache,
        KeyedAsyncLock subscribeLocks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _subscribeLocks = subscribeLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        _settings.Validate();

        return await GetCachedPlansAsync(cancellationToken);
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        _settings.Validate();

        var plan = await ResolvePlanAsync(planHandle, cancellationToken);
        var reference = MaxioCustomerReference.For(subscriber);

        // Hold same-shopper, same-plan attempts in a queue so they cannot each conclude that no
        // subscription exists yet. Racing requests on another instance are caught by the uniqueness
        // token further down.
        using var _ = await _subscribeLocks.AcquireAsync($"{reference}|{plan.Handle}", cancellationToken);

        var customer = await EnsureCustomerAsync(subscriber, reference, cancellationToken);
        var site = await GetSiteAsync(cancellationToken);
        var currency = site?.Currency;

        var existing = await FindCurrentSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Maxio customer {CustomerId} is already subscribed to {PlanHandle} (subscription {SubscriptionId}, state {State}).",
                customer.Id, plan.Handle, existing.Id, existing.State);

            return SubscriptionEnrollmentResult.Existing(MapSubscription(existing, currency));
        }

        var attributes = new MaxioSubscriptionAttributes
        {
            ProductHandle = plan.Handle,
            CustomerId = customer.Id,
            PaymentCollectionMethod = ResolvePaymentCollectionMethod(site)
        };

        try
        {
            var created = await _client.CreateSubscriptionAsync(
                attributes, BuildUniquenessToken("subscribe", reference, plan.Handle), cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on plan {PlanHandle} for customer {CustomerId} (state {State}).",
                created.Id, plan.Handle, customer.Id, created.State);

            return SubscriptionEnrollmentResult.Created(MapSubscription(created, currency));
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateSubmission)
        {
            // Maxio saw this exact subscribe already. It may or may not have completed, so re-read
            // rather than assume either way.
            _logger.LogInformation(
                "Maxio rejected a duplicate subscribe for customer {CustomerId} on plan {PlanHandle}; resolving the original request.",
                customer.Id, plan.Handle);

            var resolved = await ResolveAfterDuplicateAsync(customer.Id, plan.Handle, cancellationToken);
            if (resolved is not null)
            {
                return SubscriptionEnrollmentResult.Existing(MapSubscription(resolved, currency));
            }

            throw new SubscriptionConflictException(
                $"A subscribe request for plan '{plan.Handle}' is already in flight. Please retry in a moment.");
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        _settings.Validate();

        var customer = await _client.FindCustomerByReferenceAsync(
            MaxioCustomerReference.For(subscriber), cancellationToken);

        if (customer is null)
        {
            // The shopper has never subscribed, so no Maxio customer exists for them yet.
            return Array.Empty<SubscriptionSummary>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var currency = (await GetSiteAsync(cancellationToken))?.Currency;

        return subscriptions
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(s => MapSubscription(s, currency))
            .ToList();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetCachedPlansAsync(cancellationToken);

        var requested = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle.Trim();

        if (string.IsNullOrWhiteSpace(requested))
        {
            throw new SubscriptionPlanNotFoundException("(none specified)", plans.Select(p => p.Handle));
        }

        return plans.FirstOrDefault(p => string.Equals(p.Handle, requested, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(requested, plans.Select(p => p.Handle));
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriberIdentity subscriber, string reference, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var attributes = new MaxioCustomerAttributes
        {
            FirstName = subscriber.FirstName,
            LastName = subscriber.LastName,
            Email = subscriber.Email,
            Reference = reference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(
                attributes, BuildUniquenessToken("customer", reference, string.Empty), cancellationToken);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {Reference}.", created.Id, reference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateSubmission || ex.IndicatesReferenceTaken)
        {
            // Someone else created this customer between our lookup and our create - which is exactly
            // what the reference uniqueness constraint is there to guarantee. Read theirs.
            _logger.LogInformation(
                "Maxio already has a customer for reference {Reference}; reusing it.", reference);

            return await ReadCustomerWithRetryAsync(reference, cancellationToken)
                ?? throw new SubscriptionConflictException(
                    $"Maxio reported that customer '{reference}' already exists but did not return it. Please retry in a moment.");
        }
    }

    private async Task<MaxioCustomer?> ReadCustomerWithRetryAsync(string reference, CancellationToken cancellationToken)
    {
        return await PollAsync(
            () => _client.FindCustomerByReferenceAsync(reference, cancellationToken),
            cancellationToken);
    }

    private async Task<MaxioSubscription?> ResolveAfterDuplicateAsync(
        long customerId, string planHandle, CancellationToken cancellationToken)
    {
        return await PollAsync(
            () => FindCurrentSubscriptionAsync(customerId, planHandle, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Re-reads a value that a concurrent writer is expected to be committing, with a short bounded wait.
    /// </summary>
    private async Task<T?> PollAsync<T>(Func<Task<T?>> read, CancellationToken cancellationToken)
        where T : class
    {
        var attempts = Math.Max(1, _settings.DuplicateResolutionAttempts);
        var delay = TimeSpan.FromMilliseconds(Math.Max(0, _settings.DuplicateResolutionDelayMilliseconds));

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var value = await read();
            if (value is not null)
            {
                return value;
            }

            if (attempt < attempts && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the subscription that still ties this customer to the plan. Cancelled, expired and
    /// failed subscriptions are ignored so a shopper can subscribe again after cancelling; problem
    /// states such as past_due are not, because the shopper is still enrolled.
    /// </summary>
    private async Task<MaxioSubscription?> FindCurrentSubscriptionAsync(
        long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Where(s => SubscriptionStates.IsCurrent(s.State) &&
                        string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> GetCachedPlansAsync(CancellationToken cancellationToken)
    {
        var familyHandle = _settings.ProductFamilyHandle!;
        var cacheDuration = CacheDuration;

        if (cacheDuration <= TimeSpan.Zero)
        {
            return await LoadPlansAsync(familyHandle, cancellationToken);
        }

        var cached = await _cache.GetOrCreateAsync(PlansCacheKeyPrefix + familyHandle, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = cacheDuration;
            return LoadPlansAsync(familyHandle, cancellationToken);
        });

        return cached ?? Array.Empty<SubscriptionPlan>();
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> LoadPlansAsync(
        string familyHandle, CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioProduct> products;
        try
        {
            products = await _client.ListProductsForFamilyAsync(familyHandle, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new BillingConfigurationException(
                $"Maxio has no product family with handle '{familyHandle}'. Check '{MaxioSettings.SectionName}:ProductFamilyHandle'.");
        }

        var currency = (await GetSiteAsync(cancellationToken))?.Currency;

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => MapPlan(p, currency))
            .ToList();
    }

    /// <summary>
    /// Reads the site, which tells us the currency to report prices in and which billing architecture
    /// the site runs on. Best effort: a site read that fails must not take the plan catalog down with it.
    /// </summary>
    private async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken)
    {
        var cacheDuration = CacheDuration;

        if (cacheDuration <= TimeSpan.Zero)
        {
            return await ReadSiteAsync(cancellationToken);
        }

        return await _cache.GetOrCreateAsync(SiteCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = cacheDuration;
            return ReadSiteAsync(cancellationToken);
        });
    }

    private async Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _client.GetSiteAsync(cancellationToken);
        }
        catch (BillingException ex)
        {
            _logger.LogWarning(ex,
                "Could not read the Maxio site; falling back to the site's own defaults for currency and payment collection.");
            return null;
        }
    }

    /// <summary>
    /// Decides how Maxio should collect payment for a new subscription.
    /// </summary>
    /// <remarks>
    /// eShopOnWeb captures no payment method at signup, so the default "automatic" collection would
    /// fail the first invoice with "no payment method was on file". Invoiced collection - "remittance"
    /// on Relationship Invoicing sites, "invoice" on legacy Statements sites - is the documented way
    /// to enroll without a card. An operator that does capture a card first can override this with
    /// "Maxio:PaymentCollectionMethod".
    /// </remarks>
    private string? ResolvePaymentCollectionMethod(MaxioSite? site)
    {
        if (!string.IsNullOrWhiteSpace(_settings.PaymentCollectionMethod))
        {
            return _settings.PaymentCollectionMethod.Trim();
        }

        if (site is null)
        {
            // Unknown architecture: say nothing and let Maxio apply the site default.
            return null;
        }

        return site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
    }

    private TimeSpan CacheDuration => TimeSpan.FromSeconds(Math.Max(0, _settings.CatalogCacheSeconds));

    /// <summary>
    /// Builds the token Maxio uses to recognise a repeated write. It is derived from the operation and
    /// the shopper rather than randomly generated, so a retried or double-clicked request produces the
    /// same token; the coarse time bucket bounds how long that equivalence lasts, so a deliberate
    /// re-subscribe later on is not mistaken for a duplicate.
    /// </summary>
    private string BuildUniquenessToken(string operation, string reference, string planHandle)
    {
        var window = Math.Max(1, _settings.IdempotencyWindowSeconds);
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / window;
        var material = string.Join('|', operation, reference, planHandle, bucket.ToString(CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, string? currency) =>
        new(product.Handle!,
            string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
            product.PriceInCents,
            product.Interval,
            string.IsNullOrWhiteSpace(product.IntervalUnit) ? "month" : product.IntervalUnit!)
        {
            Description = product.Description,
            Currency = currency,
            PaymentMethodRequired = product.RequireCreditCard,
            PricePointHandle = product.ProductPricePointHandle,
            ProductFamilyHandle = product.ProductFamily?.Handle,
            HasTrial = product.TrialInterval is > 0
        };

    private static SubscriptionSummary MapSubscription(MaxioSubscription subscription, string? currency) =>
        new(subscription.Id.ToString(CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(subscription.State) ? SubscriptionStates.Pending : subscription.State!)
        {
            CustomerId = subscription.Customer?.Id.ToString(CultureInfo.InvariantCulture),
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = currency,
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            BalanceInCents = subscription.BalanceInCents,
            // next_assessment_at is when Maxio will actually try to collect; it diverges from the
            // period end after a failed payment, so it is the honest "next billing date".
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt
        };
}
