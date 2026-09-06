using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Implements <see cref="ISubscriptionService"/> against Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// Maxio is the system of record: eShopOnWeb persists no customer or subscription of its own and
/// resolves everything through references derived from the signed-in user (see
/// <see cref="MaxioReferences"/>). That keeps the integration correct across application restarts
/// and across instances, and it is what makes subscribing idempotent.
/// </remarks>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string SiteCacheKey = "maxio:site";
    private const string PlansCacheKey = "maxio:plans";

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly MaxioReferences _references;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        KeyedAsyncLock subscriberLock,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;

        // Resolving Value here runs MaxioSettingsValidator, so a misconfigured section fails on the
        // subscription endpoints rather than taking down the rest of the API at start-up.
        _settings = settings.Value;
        _references = new MaxioReferences(_settings.CustomerReferencePrefix);
        _subscriberLock = subscriberLock;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue<IReadOnlyList<SubscriptionPlan>>(PlansCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var currency = await GetSiteCurrencyAsync(cancellationToken);
        var products = await Guarded(
            () => _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken),
            $"list the plans in product family '{_settings.ProductFamilyHandle}'");

        var plans = products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => ToPlan(product, currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Cache(PlansCacheKey, (IReadOnlyList<SubscriptionPlan>)plans);
    }

    public async Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string? planHandle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var plan = await ResolvePlanAsync(planHandle, cancellationToken);
        if (plan.RequiresPaymentMethod)
        {
            throw new PaymentMethodRequiredException(plan.Handle);
        }

        var customerReference = _references.ForCustomer(subscriber);

        // Serialise this shopper's concurrent attempts so a double-click cannot race itself into two
        // customers or two subscriptions.
        using (await _subscriberLock.AcquireAsync(customerReference, cancellationToken))
        {
            var customer = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerId} already has live subscription {SubscriptionId} to plan {PlanHandle}; returning it unchanged.",
                    customer.Id, existing.Id, plan.Handle);

                return SubscribeResult.AlreadySubscribed(ToSubscription(existing, plan));
            }

            return await CreateSubscriptionAsync(customer, customerReference, plan, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customerReference = _references.ForCustomer(subscriber);
        var customer = await Guarded(
            () => _client.FindCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up the billing customer");

        if (customer is null)
        {
            // The shopper has never subscribed, so there is nothing to report. Not an error.
            return Array.Empty<Subscription>();
        }

        var subscriptions = await Guarded(
            () => _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken),
            $"list the subscriptions of billing customer {customer.Id}");

        return subscriptions
            .Select(subscription => ToSubscription(subscription, plan: null))
            .OrderByDescending(subscription => subscription.IsLive)
            .ThenByDescending(subscription => subscription.ActivatedAt ?? subscription.CurrentPeriodStartedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string? planHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var available = plans.Count == 0 ? "(none)" : string.Join(", ", plans.Select(candidate => candidate.Handle));

        var requested = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle;
        if (string.IsNullOrWhiteSpace(requested))
        {
            throw new SubscriptionPlanRequiredException(available);
        }

        var plan = plans.FirstOrDefault(candidate =>
            string.Equals(candidate.Handle, requested!.Trim(), StringComparison.OrdinalIgnoreCase));

        return plan ?? throw new SubscriptionPlanNotFoundException(requested!.Trim(), available);
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(Subscriber subscriber, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await Guarded(
            () => _client.FindCustomerByReferenceAsync(customerReference, cancellationToken),
            "look up the billing customer");

        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(subscriber);
        var request = new MaxioCreateCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = subscriber.Email,
            Reference = customerReference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, customerReference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            // Another request created the customer between our lookup and our create. Maxio's
            // uniqueness constraint on the reference is what makes that safe: re-read and continue.
            _logger.LogInformation(
                "Maxio reported reference {CustomerReference} as already taken; reusing the existing customer.", customerReference);

            var raced = await Guarded(
                () => _client.FindCustomerByReferenceAsync(customerReference, cancellationToken),
                "look up the billing customer");

            return raced ?? throw Translate(ex, "create the billing customer");
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "create the billing customer");
        }
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        MaxioCustomer customer,
        string customerReference,
        SubscriptionPlan plan,
        CancellationToken cancellationToken)
    {
        var reference = _references.ForSubscription(customerReference, plan.Handle);

        try
        {
            var created = await _client.CreateSubscriptionAsync(BuildRequest(customer, plan, reference), cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} ({State}) for customer {CustomerId} on plan {PlanHandle}.",
                created.Id, created.State, customer.Id, plan.Handle);

            return SubscribeResult.Subscribed(ToSubscription(created, plan));
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            return await ResolveReferenceCollisionAsync(customer, customerReference, plan, reference, ex, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, $"subscribe to plan '{plan.Handle}'");
        }
    }

    private MaxioCreateSubscription BuildRequest(MaxioCustomer customer, SubscriptionPlan plan, string reference) => new()
    {
        ProductHandle = plan.Handle,
        CustomerId = customer.Id,
        Reference = reference,
        PaymentCollectionMethod = _settings.PaymentCollectionMethod
    };

    /// <summary>
    /// Handles Maxio rejecting the stable subscription reference as already taken.
    /// </summary>
    /// <remarks>
    /// Two things can cause this. Either a concurrent request on another instance won the race, in
    /// which case its subscription is the correct answer; or an earlier subscription to the same
    /// plan has since been canceled or expired and still owns the reference, in which case the
    /// shopper is genuinely resubscribing and needs a fresh one.
    /// </remarks>
    private async Task<SubscribeResult> ResolveReferenceCollisionAsync(
        MaxioCustomer customer,
        string customerReference,
        SubscriptionPlan plan,
        string stableReference,
        MaxioApiException collision,
        CancellationToken cancellationToken)
    {
        var owner = await Guarded(
            () => _client.FindSubscriptionByReferenceAsync(stableReference, cancellationToken),
            "look up the subscription that already holds this reference");

        if (owner is not null && owner.Customer?.Id == customer.Id && SubscriptionStates.IsLive(owner.State))
        {
            _logger.LogInformation(
                "Subscription {SubscriptionId} already holds reference {Reference}; returning it unchanged.",
                owner.Id, stableReference);

            return SubscribeResult.AlreadySubscribed(ToSubscription(owner, plan));
        }

        var freshReference = _references.ForResubscription(customerReference, plan.Handle);
        _logger.LogInformation(
            "Reference {Reference} is held by a subscription that is no longer live ({Reason}); resubscribing customer {CustomerId} as {NewReference}.",
            stableReference, collision.Errors.FirstOrDefault() ?? "reference taken", customer.Id, freshReference);

        try
        {
            var created = await _client.CreateSubscriptionAsync(BuildRequest(customer, plan, freshReference), cancellationToken);
            return SubscribeResult.Subscribed(ToSubscription(created, plan));
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, $"subscribe to plan '{plan.Handle}'");
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await Guarded(
            () => _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken),
            $"list the subscriptions of billing customer {customerId}");

        return subscriptions
            .Where(subscription =>
                SubscriptionStates.IsLive(subscription.State) &&
                string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .FirstOrDefault();
    }

    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<string>(SiteCacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached!;
        }

        var site = await Guarded(() => _client.GetSiteAsync(cancellationToken), "read the billing site");
        var currency = string.IsNullOrWhiteSpace(site.Currency) ? "USD" : site.Currency!;

        return Cache(SiteCacheKey, currency);
    }

    private T Cache<T>(string key, T value)
    {
        if (_settings.CatalogCacheSeconds > 0)
        {
            _cache.Set(key, value, TimeSpan.FromSeconds(_settings.CatalogCacheSeconds));
        }

        return value;
    }

    private SubscriptionPlan ToPlan(MaxioProduct product, string currency) => new(
        handle: product.Handle!,
        name: string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        description: product.Description,
        priceInCents: product.PriceInCents ?? 0,
        currency: currency,
        interval: product.Interval ?? 0,
        intervalUnit: product.IntervalUnit ?? string.Empty,
        productFamilyHandle: product.ProductFamily?.Handle ?? _settings.ProductFamilyHandle,
        requiresPaymentMethod: product.RequireCreditCard ?? false,
        trialInterval: product.TrialInterval,
        trialIntervalUnit: product.TrialIntervalUnit);

    private static Subscription ToSubscription(MaxioSubscription subscription, SubscriptionPlan? plan) => new(
        id: subscription.Id,
        reference: subscription.Reference,
        state: string.IsNullOrWhiteSpace(subscription.State) ? SubscriptionStates.Pending : subscription.State!,
        planHandle: subscription.Product?.Handle ?? plan?.Handle ?? string.Empty,
        planName: subscription.Product?.Name ?? plan?.Name ?? string.Empty,
        priceInCents: subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? plan?.PriceInCents ?? 0,
        currency: subscription.Currency ?? plan?.Currency ?? "USD",
        interval: subscription.Product?.Interval ?? plan?.Interval ?? 0,
        intervalUnit: subscription.Product?.IntervalUnit ?? plan?.IntervalUnit ?? string.Empty,
        balanceInCents: subscription.BalanceInCents ?? 0,
        currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
        currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        nextBillingAt: subscription.NextAssessmentAt,
        activatedAt: subscription.ActivatedAt,
        canceledAt: subscription.CanceledAt,
        customerId: subscription.Customer?.Id ?? 0,
        customerReference: subscription.Customer?.Reference);

    private static (string FirstName, string LastName) SplitName(Subscriber subscriber)
    {
        // Maxio requires a first and last name on a customer. eShopOnWeb's Identity user carries
        // neither, so fall back to the local part of the email address.
        var first = subscriber.FirstName;
        var last = subscriber.LastName;

        if (string.IsNullOrWhiteSpace(first))
        {
            var localPart = subscriber.Email.Split('@')[0];
            first = string.IsNullOrWhiteSpace(localPart) ? subscriber.UserName : localPart;
        }

        if (string.IsNullOrWhiteSpace(last))
        {
            last = "eShopOnWeb";
        }

        return (first!.Trim(), last!.Trim());
    }

    private static async Task<T> Guarded<T>(Func<Task<T>> call, string action)
    {
        try
        {
            return await call();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, action);
        }
    }

    private static BillingProviderException Translate(MaxioApiException ex, string action) =>
        new($"Maxio could not {action}.", (int)ex.StatusCode, ex.Errors);
}
