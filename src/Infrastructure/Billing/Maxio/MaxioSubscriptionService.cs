using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainSubscription = Microsoft.eShopWeb.ApplicationCore.Subscriptions.Subscription;
using WireSubscription = Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire.MaxioSubscription;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Implements <see cref="ISubscriptionService"/> against Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// <para>
/// Maxio is the system of record: eShopOnWeb persists nothing about plans, billing customers or
/// enrollments. The join between the two systems is the Maxio customer <c>reference</c>, which is
/// derived from the shopper's login. Because Maxio allows at most one customer per reference,
/// "ensure a customer exists" is naturally idempotent - even across application instances and
/// even though eShopOnWeb's own database may be in-memory and thrown away on restart.
/// </para>
/// <para>
/// Subscribing is idempotent in three layers: a per-shopper in-process lock keeps a double-click
/// from racing itself, a reconciliation read against Maxio returns the existing enrollment
/// instead of creating a second one, and the create itself carries a <c>uniqueness_token</c> so
/// Maxio rejects a replay that arrives by any other route.
/// </para>
/// <para>
/// Plans are addressed by handle throughout. Maxio reassigns numeric ids when a catalog is
/// re-seeded, so nothing here may depend on them.
/// </para>
/// </remarks>
public sealed class MaxioSubscriptionService : ISubscriptionService
{
    private const string SiteCacheKey = "maxio:site";
    private static readonly TimeSpan SiteCacheDuration = TimeSpan.FromMinutes(30);

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscribeLock;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioOptions> options,
        IMemoryCache cache,
        KeyedAsyncLock subscribeLock,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _subscribeLock = subscribeLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var options = Configured();

        return await GuardProviderCallAsync(async () =>
        {
            var plans = await GetPlansInternalAsync(options, cancellationToken).ConfigureAwait(false);
            return (IReadOnlyList<SubscriptionPlan>)plans
                .OrderBy(p => p.Price)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = Configured();
        var subscriber = request.Subscriber.Validated();
        var planHandle = (request.PlanHandle ?? string.Empty).Trim();

        if (planHandle.Length == 0)
        {
            throw new BillingValidationException("A plan handle is required to subscribe.");
        }

        return await GuardProviderCallAsync(
            () => SubscribeInternalAsync(options, subscriber, planHandle, request.IdempotencyKey, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DomainSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var options = Configured();
        var reference = MaxioCustomerMapping.CustomerReference(options.CustomerReferencePrefix, subscriber.Validated());

        return await GuardProviderCallAsync(async () =>
        {
            var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken)
                .ConfigureAwait(false);

            if (customer is null)
            {
                // Never subscribed: an empty list, not an error.
                return (IReadOnlyList<DomainSubscription>)Array.Empty<DomainSubscription>();
            }

            var currency = await GetCurrencyAsync(cancellationToken).ConfigureAwait(false);
            var plansByHandle = await GetPlanIndexAsync(options, cancellationToken).ConfigureAwait(false);

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
                .ConfigureAwait(false);

            return subscriptions
                .Select(s => Map(s, currency, reference, plansByHandle))
                .OrderByDescending(s => s.CreatedAt)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SubscribeResult> SubscribeInternalAsync(
        MaxioOptions options,
        SubscriberIdentity subscriber,
        string planHandle,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var plansByHandle = await GetPlanIndexAsync(options, cancellationToken).ConfigureAwait(false);

        if (!plansByHandle.TryGetValue(planHandle, out var plan))
        {
            throw new SubscriptionPlanNotFoundException(planHandle, options.ProductFamilyHandle!);
        }

        if (plan.RequiresPaymentMethod)
        {
            // Signing up for such a plan needs tokenized card details, which this integration
            // deliberately does not collect. Fail with something actionable rather than letting
            // Maxio reject the create with a generic 422.
            throw new BillingValidationException(
                $"Plan '{plan.Handle}' requires a stored payment method before signup, which this " +
                "endpoint does not collect. Configure the plan with 'payment method not required', " +
                "or add payment capture before subscribing to it.");
        }

        var site = await GetSiteAsync(cancellationToken).ConfigureAwait(false);
        var currency = string.IsNullOrWhiteSpace(site.Currency) ? "USD" : site.Currency!.Trim().ToUpperInvariant();
        var customerReference = MaxioCustomerMapping.CustomerReference(options.CustomerReferencePrefix, subscriber);

        // Serialise per shopper so a double-clicked Subscribe button cannot run the
        // reconcile-then-create sequence twice concurrently in this process.
        using var _ = await _subscribeLock.AcquireAsync(customerReference, cancellationToken).ConfigureAwait(false);

        var customer = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken).ConfigureAwait(false);

        var existingForPlan = await GetSubscriptionsForPlanAsync(customer.Id, planHandle, cancellationToken)
            .ConfigureAwait(false);

        var live = existingForPlan.FirstOrDefault(s => MaxioSubscriptionMapper.OccupiesPlan(
            MaxioSubscriptionMapper.ToState(s.State)));

        if (live is not null)
        {
            _logger.LogInformation(
                "Shopper {CustomerReference} is already on plan {PlanHandle} (subscription {SubscriptionId}, state {State}); returning the existing subscription.",
                customerReference, planHandle, live.Id, live.State);

            return new SubscribeResult(Map(live, currency, customerReference, plansByHandle), Created: false);
        }

        // Every prior enrollment on this plan has ended, so this signup is the next "generation".
        // Folding that into the uniqueness token keeps a legitimate re-subscribe from looking
        // like a replay of the original one.
        var generation = existingForPlan.Count;

        var attributes = new MaxioCreateSubscriptionAttributes
        {
            ProductHandle = plan.Handle,
            CustomerId = customer.Id,
            Reference = MaxioCustomerMapping.SubscriptionReference(customerReference, plan.Handle, generation + 1),
            UniquenessToken = MaxioCustomerMapping.UniquenessToken(
                customerReference, plan.Handle, generation, idempotencyKey),
            PaymentCollectionMethod = ResolvePaymentCollectionMethod(options, site)
        };

        try
        {
            var created = await _client.CreateSubscriptionAsync(attributes, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on plan {PlanHandle} for {CustomerReference} (state {State}).",
                created.Id, planHandle, customerReference, created.State);

            return new SubscribeResult(Map(created, currency, customerReference, plansByHandle), Created: true);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateSubmission)
        {
            // Maxio saw this exact signup before. Either an earlier attempt succeeded and its
            // response was lost, or a concurrent request beat us here - re-read to find out which.
            _logger.LogWarning(
                "Maxio rejected the signup for {CustomerReference} on {PlanHandle} as a duplicate submission; reconciling.",
                customerReference, planHandle);

            var reconciled = (await GetSubscriptionsForPlanAsync(customer.Id, planHandle, cancellationToken)
                    .ConfigureAwait(false))
                .FirstOrDefault(s => MaxioSubscriptionMapper.OccupiesPlan(MaxioSubscriptionMapper.ToState(s.State)));

            if (reconciled is not null)
            {
                return new SubscribeResult(Map(reconciled, currency, customerReference, plansByHandle), Created: false);
            }

            throw new DuplicateBillingRequestException(
                $"Maxio recognised this signup for plan '{planHandle}' as a duplicate of a recent request, but no " +
                "matching subscription could be found. The original request may still be settling - re-read " +
                "/api/my-subscriptions before retrying.");
        }
    }

    /// <summary>
    /// Looks the customer up by reference and creates it when absent. The 422 branch covers the
    /// race where another request created the same customer between the read and the write.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriberIdentity subscriber,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var attributes = MaxioCustomerMapping.ToCustomerAttributes(subscriber, customerReference);

        try
        {
            var created = await _client.CreateCustomerAsync(attributes, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id, customerReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsReferenceTaken)
        {
            var raced = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken)
                .ConfigureAwait(false);

            if (raced is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerId} for {CustomerReference} was created concurrently; reusing it.",
                    raced.Id, customerReference);
                return raced;
            }

            throw;
        }
    }

    private async Task<List<WireSubscription>> GetSubscriptionsForPlanAsync(
        long customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        return subscriptions
            .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private DomainSubscription Map(
        WireSubscription subscription,
        string currency,
        string customerReference,
        IReadOnlyDictionary<string, SubscriptionPlan> plansByHandle)
    {
        var handle = subscription.Product?.Handle;
        var plan = handle is not null && plansByHandle.TryGetValue(handle, out var match) ? match : null;

        var mapped = MaxioSubscriptionMapper.ToSubscription(subscription, currency, customerReference, plan);

        if (mapped.State == SubscriptionState.Unknown)
        {
            _logger.LogWarning(
                "Maxio subscription {SubscriptionId} reported unrecognised state '{ProviderState}'; treating it as live.",
                mapped.Id, mapped.ProviderState);
        }

        return mapped;
    }

    private async Task<IReadOnlyDictionary<string, SubscriptionPlan>> GetPlanIndexAsync(
        MaxioOptions options,
        CancellationToken cancellationToken)
    {
        var plans = await GetPlansInternalAsync(options, cancellationToken).ConfigureAwait(false);

        var index = new Dictionary<string, SubscriptionPlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            index[plan.Handle] = plan;
        }

        return index;
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansInternalAsync(
        MaxioOptions options,
        CancellationToken cancellationToken)
    {
        var familyHandle = options.ProductFamilyHandle!;
        var cacheKey = $"maxio:plans:{options.ResolveBaseAddress()}:{familyHandle}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var currency = await GetCurrencyAsync(cancellationToken).ConfigureAwait(false);

        var products = await _client.ListProductsForFamilyAsync(familyHandle, cancellationToken)
            .ConfigureAwait(false);

        var plans = products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MaxioSubscriptionMapper.ToPlan(p, currency))
            .ToList();

        if (plans.Count == 0)
        {
            _logger.LogWarning(
                "Maxio product family '{ProductFamilyHandle}' returned no active products; /api/subscription-plans will be empty.",
                familyHandle);
        }

        _cache.Set(cacheKey, (IReadOnlyList<SubscriptionPlan>)plans,
            TimeSpan.FromSeconds(Math.Max(1, options.CatalogCacheSeconds)));

        return plans;
    }

    /// <summary>
    /// The site record, cached. It supplies the billing currency (Maxio reports prices in minor
    /// units without one) and the subscription architecture, which decides the valid payment
    /// collection methods.
    /// </summary>
    private async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCacheKey, out MaxioSite? cached) && cached is not null)
        {
            return cached;
        }

        var site = await _client.GetSiteAsync(cancellationToken).ConfigureAwait(false);
        _cache.Set(SiteCacheKey, site, SiteCacheDuration);
        return site;
    }

    private async Task<string> GetCurrencyAsync(CancellationToken cancellationToken)
    {
        var site = await GetSiteAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(site.Currency) ? "USD" : site.Currency!.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// The configured collection method, or the invoice-style one matching the site's
    /// architecture. Automatic collection is not a viable default: it charges at signup, and no
    /// payment method is captured anywhere in this flow.
    /// </summary>
    private static string ResolvePaymentCollectionMethod(MaxioOptions options, MaxioSite site)
    {
        if (!string.IsNullOrWhiteSpace(options.PaymentCollectionMethod))
        {
            return options.PaymentCollectionMethod!.Trim().ToLowerInvariant();
        }

        return site.RelationshipInvoicingEnabled ? "remittance" : "invoice";
    }

    private MaxioOptions Configured()
    {
        var options = _options.CurrentValue;
        options.EnsureConfigured();
        return options;
    }

    /// <summary>
    /// Converts transport and Maxio-specific faults into the provider-neutral billing exceptions
    /// declared in ApplicationCore, so nothing above this layer has to know about Maxio.
    /// </summary>
    private async Task<T> GuardProviderCallAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (MaxioApiException ex)
        {
            _logger.LogError(ex, "Maxio rejected {Method} /{Path} with {StatusCode}.", ex.Method, ex.Path, (int)ex.StatusCode);

            throw ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new BillingProviderException(
                    "Maxio rejected the configured API credentials. Check Maxio:ApiKey and Maxio:Subdomain.", ex),

                HttpStatusCode.UnprocessableEntity => new BillingValidationException(
                    ex.Errors.Count > 0
                        ? "Maxio rejected the request: " + string.Join("; ", ex.Errors)
                        : "Maxio rejected the request as invalid.",
                    ex.Errors),

                _ => new BillingProviderException(ex.Message, ex)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _logger.LogError(ex, "Could not reach the Maxio API.");
            throw new BillingProviderException(
                "The Maxio API could not be reached. The request was not applied; it is safe to retry.", ex);
        }
    }
}
