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
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements recurring-subscription billing on top of Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// <para>
/// Maxio is the system of record: eShopOnWeb keeps no local copy of customers or subscriptions. The link
/// between the two systems is the Maxio customer <c>reference</c>, which carries the shopper's
/// <see cref="Subscriber.Reference"/>. Maxio enforces uniqueness on that value, which is what makes
/// "ensure a customer exists" safe to repeat.
/// </para>
/// <para>
/// Plans are the products published in the configured product family. Products outside the family are not
/// subscribable through this API, so a caller cannot enroll on an arbitrary product of the Maxio site.
/// </para>
/// </remarks>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlansCacheKey = "maxio:plans";
    private const string SiteCacheKey = "maxio:site";

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioSettings> settings,
        IMemoryCache cache,
        KeyedAsyncLock subscriberLock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _subscriberLock = subscriberLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        await GetPlansAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        var plan = await ResolvePlanAsync(planHandle, cancellationToken).ConfigureAwait(false);

        // eShopOnWeb never collects card or bank details, so a plan that demands a stored payment profile
        // cannot be signed up for here. Fail before touching Maxio rather than creating a customer we
        // would then be unable to enroll.
        if (plan.RequiresPaymentMethod)
        {
            throw new PaymentMethodRequiredException(plan.Handle);
        }

        try
        {
            // Serialise this shopper's subscribe attempts so a double-click cannot race the existence check.
            using var _ = await _subscriberLock.AcquireAsync(subscriber.Reference, cancellationToken).ConfigureAwait(false);

            var customer = await EnsureCustomerAsync(subscriber, cancellationToken).ConfigureAwait(false);
            var existingSubscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

            var samePlan = existingSubscriptions
                .Where(subscription => string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var live = samePlan.FirstOrDefault(subscription => SubscriptionStates.IsLive(subscription.State));
            if (live is not null)
            {
                _logger.LogInformation(
                    "Subscriber {SubscriberReference} is already enrolled on plan {PlanHandle} (subscription {SubscriptionId}, state {State}); returning the existing subscription.",
                    subscriber.Reference,
                    plan.Handle,
                    live.Id,
                    live.State);

                return new SubscribeResult(await MapAsync(live, cancellationToken).ConfigureAwait(false), AlreadyExisted: true);
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = BuildSubscriptionReference(subscriber, plan.Handle, samePlan.Length),
                    PaymentCollectionMethod = await ResolveCollectionMethodAsync(cancellationToken).ConfigureAwait(false)
                }
            };

            var created = await _client.CreateSubscriptionAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created subscription {SubscriptionId} on plan {PlanHandle} for subscriber {SubscriberReference} (state {State}).",
                created.Id,
                plan.Handle,
                subscriber.Reference,
                created.State);

            return new SubscribeResult(await MapAsync(created, cancellationToken).ConfigureAwait(false), AlreadyExisted: false);
        }
        catch (MaxioApiException ex)
        {
            throw ToBillingException(ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(subscriber, nameof(subscriber));

        try
        {
            var customer = await _client.ReadCustomerByReferenceAsync(subscriber.Reference, cancellationToken).ConfigureAwait(false);
            if (customer is null)
            {
                // The shopper has never subscribed, so no customer record exists yet. That is not an error.
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken).ConfigureAwait(false);

            var mapped = new List<CustomerSubscription>(subscriptions.Count);
            foreach (var subscription in subscriptions)
            {
                mapped.Add(await MapAsync(subscription, cancellationToken).ConfigureAwait(false));
            }

            return mapped
                .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(subscription => subscription.Id)
                .ToArray();
        }
        catch (MaxioApiException ex)
        {
            throw ToBillingException(ex);
        }
    }

    /// <summary>
    /// Returns the Maxio customer carrying the subscriber's reference, creating it when absent.
    /// </summary>
    /// <remarks>
    /// Maxio rejects a duplicate customer reference with 422, which turns the create into a safe upsert:
    /// if a concurrent caller (or another replica) won the race, we re-read instead of failing.
    /// </remarks>
    private async Task<MaxioCustomer> EnsureCustomerAsync(Subscriber subscriber, CancellationToken cancellationToken)
    {
        var existing = await _client.ReadCustomerByReferenceAsync(subscriber.Reference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = subscriber.FirstName,
                LastName = subscriber.LastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for subscriber {SubscriberReference}.",
                created.Id,
                subscriber.Reference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var raced = await _client.ReadCustomerByReferenceAsync(subscriber.Reference, cancellationToken).ConfigureAwait(false);
            if (raced is null)
            {
                throw;
            }

            _logger.LogInformation(
                "Maxio customer {CustomerId} for subscriber {SubscriberReference} was created concurrently; reusing it.",
                raced.Id,
                subscriber.Reference);

            return raced;
        }
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plan = Find(await GetPlansAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false));
        if (plan is not null)
        {
            return plan;
        }

        // The catalogue is cached, so an unknown handle may simply be a plan published moments ago.
        plan = Find(await GetPlansAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false));

        return plan ?? throw new SubscriptionPlanNotFoundException(planHandle);

        SubscriptionPlan? Find(IReadOnlyList<SubscriptionPlan> plans) =>
            plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cache.TryGetValue(PlansCacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var settings = _settings.CurrentValue;
        var site = await GetSiteAsync(forceRefresh, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<MaxioProduct> products;
        try
        {
            // The specification accepts either the family id or its handle prefixed with "handle:".
            products = await _client
                .ListProductsForProductFamilyAsync($"handle:{settings.ProductFamilyHandle}", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            throw ToBillingException(ex);
        }

        var plans = products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => MapPlan(product, site))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Cache(PlansCacheKey, (IReadOnlyList<SubscriptionPlan>)plans);

        return plans;
    }

    private async Task<MaxioSite> GetSiteAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cache.TryGetValue(SiteCacheKey, out MaxioSite? cached) && cached is not null)
        {
            return cached;
        }

        MaxioSite site;
        try
        {
            site = await _client.ReadSiteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            throw ToBillingException(ex);
        }

        Cache(SiteCacheKey, site);

        return site;
    }

    private void Cache<T>(string key, T value)
    {
        var seconds = _settings.CurrentValue.CatalogCacheSeconds;
        if (seconds <= 0)
        {
            return;
        }

        _cache.Set(key, value, TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// Chooses how Maxio should collect payment for a new subscription.
    /// </summary>
    /// <remarks>
    /// The site default is normally <c>automatic</c>, which requires a payment profile and fails signup
    /// with "No payment method was on file" for any priced plan. eShopOnWeb does not capture payment
    /// instruments, so subscriptions are created for invoice-style collection instead. The specification
    /// notes that the accepted value depends on the site's architecture: <c>remittance</c> under
    /// Relationship Invoicing, <c>invoice</c> under the legacy Statements architecture.
    /// </remarks>
    private async Task<string> ResolveCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var site = await GetSiteAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);

        return site.RelationshipInvoicingEnabled ? CollectionMethods.Remittance : CollectionMethods.Invoice;
    }

    /// <summary>
    /// Builds the reference stored on the Maxio subscription: readable, and derived from the shopper and
    /// plan so it can be traced back to this application. The ordinal keeps it unique when a shopper
    /// re-subscribes to a plan they previously left.
    /// </summary>
    private static string BuildSubscriptionReference(Subscriber subscriber, string planHandle, int priorSubscriptionsOnPlan)
    {
        var reference = $"{subscriber.Reference}:{planHandle}";

        return priorSubscriptionsOnPlan == 0 ? reference : $"{reference}:{priorSubscriptionsOnPlan + 1}";
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, MaxioSite site) => new()
    {
        Id = product.Id,
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        PriceInCents = product.PriceInCents,
        Currency = site.Currency ?? string.Empty,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        SetupFeeInCents = product.InitialChargeInCents,
        RequiresPaymentMethod = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private async Task<CustomerSubscription> MapAsync(MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var currency = subscription.Currency;
        if (string.IsNullOrWhiteSpace(currency))
        {
            var site = await GetSiteAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
            currency = site.Currency ?? string.Empty;
        }

        return new CustomerSubscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = currency,
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            TrialEndedAt = subscription.TrialEndedAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt,
            BalanceInCents = subscription.BalanceInCents,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CustomerId = subscription.Customer?.Id ?? 0,
            CustomerReference = subscription.Customer?.Reference
        };
    }

    private static BillingProviderException ToBillingException(MaxioApiException exception) =>
        new(exception.Message, (int)exception.StatusCode, exception.Errors, exception);
}
