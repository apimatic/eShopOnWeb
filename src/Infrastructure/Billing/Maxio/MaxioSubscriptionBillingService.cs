using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// Maxio is the system of record: eShopOnWeb persists no customer or subscription rows, so there is
/// nothing to reconcile and nothing to lose on restart. Identity flows in from the caller's token as
/// a <see cref="BillingSubscriber"/> and is projected onto Maxio's <c>reference</c> fields by
/// <see cref="MaxioReferences"/>.
/// </remarks>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string SiteCacheKey = "maxio:site";
    private const string PlansCacheKeyPrefix = "maxio:plans:";

    private readonly IMaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscriberLocks;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        KeyedAsyncLock subscriberLocks,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _subscriberLocks = subscriberLocks;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();

        var familyHandle = _options.ProductFamilyHandle!.Trim();
        var cacheKey = PlansCacheKeyPrefix + familyHandle;

        if (_options.CatalogCacheSeconds > 0 && _cache.TryGetValue(cacheKey, out IReadOnlyCollection<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var plans = await LoadPlansAsync(familyHandle, cancellationToken).ConfigureAwait(false);

        if (_options.CatalogCacheSeconds > 0)
        {
            _cache.Set(cacheKey, plans, TimeSpan.FromSeconds(_options.CatalogCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscribeToPlanResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _options.Validate();

        ArgumentNullException.ThrowIfNull(request.Subscriber);
        var subscriber = request.Subscriber;
        subscriber.Validate();

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new BillingValidationException("A plan handle is required.");
        }

        var collectionMethod = ResolveCollectionMethod(request.PaymentCollectionMethod);

        // Only plans in the configured product family may be subscribed to. This both gives the
        // caller a clear 404 for a typo and stops an arbitrary product handle on the Maxio site
        // from being reachable through this endpoint.
        var plan = (await GetPlansAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(request.PlanHandle.Trim());

        // Serialise this shopper's subscribe attempts so a double-click cannot race the
        // "already subscribed?" check against the create that follows it.
        using var _ = await _subscriberLocks.AcquireAsync(
            MaxioReferences.Customer(_options.ReferencePrefix, subscriber.Key), cancellationToken).ConfigureAwait(false);

        return await ExecuteAsync(async () =>
        {
            var customer = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);
            var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

            var forThisPlan = existing
                .Where(s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var live = forThisPlan.FirstOrDefault(s => SubscriptionStates.IsLive(s.State));
            if (live is not null)
            {
                _logger.LogInformation(
                    "Subscriber {Reference} is already subscribed to {PlanHandle} (Maxio subscription {SubscriptionId}, state {State}); returning the existing subscription.",
                    customer.Reference,
                    plan.Handle,
                    live.Id,
                    live.State);

                return new SubscribeToPlanResult(MapSubscription(live, customer), Created: false);
            }

            // Retired subscriptions still hold their reference, so the next enrolment takes the
            // next generation. Computed under the lock, so two concurrent callers on this host
            // cannot pick the same one; two callers on different hosts can, and Maxio's uniqueness
            // constraint resolves that below.
            var reference = MaxioReferences.Subscription(
                _options.ReferencePrefix, subscriber.Key, plan.Handle, forThisPlan.Count);

            var created = await CreateSubscriptionAsync(customer, plan.Handle, reference, collectionMethod, cancellationToken)
                .ConfigureAwait(false);

            return created;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(
        BillingSubscriber subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        _options.Validate();

        if (string.IsNullOrWhiteSpace(subscriber.Key))
        {
            throw new ArgumentException("A billing subscriber key is required.", nameof(subscriber));
        }

        return await ExecuteAsync(async () =>
        {
            var reference = MaxioReferences.Customer(_options.ReferencePrefix, subscriber.Key);
            var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);

            if (customer is null)
            {
                // The shopper has never subscribed. Reading must not create a billing record.
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

            return subscriptions
                .Select(s => MapSubscription(s, customer))
                .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
                .ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyCollection<SubscriptionPlan>> LoadPlansAsync(string familyHandle, CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var products = await _client.ListProductsForFamilyAsync(familyHandle, cancellationToken).ConfigureAwait(false);

            if (products is null)
            {
                throw new BillingConfigurationException(
                    $"Maxio has no product family with handle '{familyHandle}'. Check '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}'.");
            }

            var currency = await GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

            return products
                .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
                .Select(p => new SubscriptionPlan
                {
                    Handle = p.Handle!,
                    Name = p.Name ?? p.Handle!,
                    Description = p.Description,
                    ProductFamilyHandle = p.ProductFamily?.Handle ?? familyHandle,
                    PriceInCents = p.PriceInCents,
                    Currency = currency,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit,
                    RequiresPaymentMethod = p.RequireCreditCard,
                    TrialPriceInCents = p.TrialPriceInCents,
                    TrialInterval = p.TrialInterval,
                    TrialIntervalUnit = p.TrialIntervalUnit,
                    PricePointHandle = p.ProductPricePointHandle,
                })
                .OrderBy(p => p.PriceInCents)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The site's default currency. Maxio does not return a currency on the product payload, so it
    /// is read once from the site and cached alongside the catalogue.
    /// </summary>
    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_options.CatalogCacheSeconds > 0
            && _cache.TryGetValue(SiteCacheKey, out string? cached)
            && !string.IsNullOrEmpty(cached))
        {
            return cached!;
        }

        var site = await _client.GetSiteAsync(cancellationToken).ConfigureAwait(false);
        var currency = site.Currency ?? string.Empty;

        if (_options.CatalogCacheSeconds > 0)
        {
            _cache.Set(SiteCacheKey, currency, TimeSpan.FromSeconds(_options.CatalogCacheSeconds));
        }

        return currency;
    }

    /// <summary>
    /// Returns the shopper's Maxio customer, creating it on first use.
    /// </summary>
    /// <remarks>
    /// The lookup-then-create is racy on its own, so the create also handles Maxio rejecting the
    /// reference as taken: that rejection means a concurrent caller won, and the winner is fetched
    /// and returned. Either way at most one customer exists per shopper.
    /// </remarks>
    private async Task<MaxioCustomer> EnsureCustomerAsync(BillingSubscriber subscriber, CancellationToken cancellationToken)
    {
        var reference = MaxioReferences.Customer(_options.ReferencePrefix, subscriber.Key);

        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.CreateCustomerAsync(
                new MaxioCreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Organization = string.IsNullOrWhiteSpace(subscriber.Organization) ? null : subscriber.Organization,
                    Reference = reference,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Created Maxio customer {CustomerId} for {Reference}.", created.Id, reference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            _logger.LogInformation(
                "Maxio customer {Reference} was created concurrently; using the existing record.", reference);

            return await _client.FindCustomerByReferenceAsync(reference, cancellationToken).ConfigureAwait(false)
                ?? throw new BillingProviderException(
                    $"Maxio reported customer reference '{reference}' as taken but the record could not be read back.",
                    ex);
        }
    }

    /// <summary>
    /// Creates the subscription, treating Maxio's uniqueness rejection as "someone else already
    /// created exactly this enrolment" rather than as an error.
    /// </summary>
    private async Task<SubscribeToPlanResult> CreateSubscriptionAsync(
        MaxioCustomer customer,
        string planHandle,
        string reference,
        string collectionMethod,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _client.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customer.Id,
                    Reference = reference,
                    PaymentCollectionMethod = collectionMethod,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({Reference}) on {PlanHandle} for customer {CustomerId}; state {State}, next billing {NextBillingAt}.",
                created.Id,
                reference,
                planHandle,
                customer.Id,
                created.State,
                created.NextAssessmentAt);

            return new SubscribeToPlanResult(MapSubscription(created, customer), Created: true);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            _logger.LogInformation(
                "Maxio subscription {Reference} was created concurrently; using the existing record.", reference);

            var existing = await _client.FindSubscriptionByReferenceAsync(reference, cancellationToken).ConfigureAwait(false)
                ?? throw new BillingProviderException(
                    $"Maxio reported subscription reference '{reference}' as taken but the record could not be read back.",
                    ex);

            return new SubscribeToPlanResult(MapSubscription(existing, customer), Created: false);
        }
    }

    private string ResolveCollectionMethod(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return _options.PaymentCollectionMethod.Trim().ToLowerInvariant();
        }

        if (!MaxioOptions.IsSupportedCollectionMethod(requested))
        {
            throw new BillingValidationException(
                $"'{requested}' is not a supported payment collection method. Supported values: {string.Join(", ", MaxioOptions.AllowedCollectionMethods)}.");
        }

        return requested!.Trim().ToLowerInvariant();
    }

    private static CustomerSubscription MapSubscription(MaxioSubscription source, MaxioCustomer? customer) =>
        new()
        {
            Id = source.Id,
            Reference = source.Reference,
            State = source.State ?? string.Empty,
            // Null on Maxio sites using the new Catalog experience, where a subscription need not
            // be tied to a product.
            PlanHandle = source.Product?.Handle,
            PlanName = source.Product?.Name,
            PriceInCents = source.ProductPriceInCents,
            Currency = source.Currency,
            Interval = source.Product?.Interval ?? 0,
            IntervalUnit = source.Product?.IntervalUnit,
            CurrentPeriodStartsAt = source.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = source.CurrentPeriodEndsAt,
            NextBillingAt = source.NextAssessmentAt,
            CreatedAt = source.CreatedAt,
            ActivatedAt = source.ActivatedAt,
            CanceledAt = source.CanceledAt,
            ExpiresAt = source.ExpiresAt,
            BalanceInCents = source.BalanceInCents,
            PaymentCollectionMethod = source.PaymentCollectionMethod,
            CustomerId = source.Customer?.Id ?? customer?.Id ?? 0,
            CustomerReference = source.Customer?.Reference ?? customer?.Reference,
        };

    /// <summary>
    /// Runs a unit of Maxio work, translating transport and API failures into the billing
    /// exceptions the API layer knows how to answer with.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (MaxioApiException ex) when (ex.IsValidationFailure)
        {
            // Maxio rejected the payload. The only inputs this integration lets a caller choose are
            // the plan handle and the collection method, so this is the caller's problem to fix.
            throw new BillingValidationException(
                ex.Errors.Count > 0
                    ? string.Join(" ", ex.Errors)
                    : "Maxio rejected the request.",
                ex.Errors);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(
                ex,
                "Maxio rejected the configured credentials. Check '{Section}:{Key}'.",
                MaxioOptions.SectionName,
                nameof(MaxioOptions.ApiKey));

            throw new BillingConfigurationException(
                $"Maxio rejected the configured API credentials ('{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}').");
        }
        catch (MaxioApiException ex)
        {
            throw new BillingProviderException(ex.Message, ex, (int)ex.StatusCode);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BillingProviderException("The Maxio API did not respond in time.", ex, isTimeout: true);
        }
        catch (HttpRequestException ex)
        {
            throw new BillingProviderException("The Maxio API could not be reached.", ex, isTimeout: true);
        }
    }
}
