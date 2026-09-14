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
/// Subscription billing backed by Maxio Advanced Billing, which is the system of record: eShopOnWeb
/// stores no copy of a shopper's plans and answers every read from Maxio.
/// </summary>
/// <remarks>
/// <para>
/// A shopper is linked to Maxio by a deterministic customer <c>reference</c> derived from their
/// eShopOnWeb identity, so the link survives restarts and needs no local table. Enrolment is
/// idempotent in three layers:
/// </para>
/// <list type="number">
///   <item>an in-process lock per shopper and plan, which absorbs a double-click;</item>
///   <item>a read of the shopper's current subscriptions, which is authoritative across instances;</item>
///   <item>a guard on the write itself — the unique customer reference for customers, and a
///   short-lived <c>uniqueness_token</c> for subscriptions, whose references Maxio does not constrain.</item>
/// </list>
/// </remarks>
internal sealed class MaxioSubscriptionService : ISubscriptionPlanService, ISubscriptionService
{
    private const string PlanCacheKeyPrefix = "maxio:plans:";

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly MaxioReferenceFactory _references;
    private readonly KeyedAsyncLock _enrolmentLock;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioSettings> settings,
        MaxioReferenceFactory references,
        KeyedAsyncLock enrolmentLock,
        IMemoryCache cache,
        TimeProvider timeProvider,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _references = references;
        _enrolmentLock = enrolmentLock;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var settings = EnsureConfigured();
        var familyHandle = settings.ProductFamilyHandle!;
        var cacheKey = PlanCacheKeyPrefix + familyHandle;

        if (settings.PlanCacheSeconds > 0 &&
            _cache.TryGetValue<IReadOnlyList<SubscriptionPlan>>(cacheKey, out var cached) &&
            cached is not null)
        {
            return cached;
        }

        var plans = await LoadPlansAsync(familyHandle, cancellationToken);

        if (settings.PlanCacheSeconds > 0)
        {
            _cache.Set(cacheKey, plans, TimeSpan.FromSeconds(settings.PlanCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(string handle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(plan => string.Equals(plan.Handle, handle.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SubscribeResult> SubscribeAsync(
        Subscriber subscriber,
        string? planHandle,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var settings = EnsureConfigured();
        var plan = await ResolvePlanAsync(planHandle, settings, cancellationToken);
        var customerReference = _references.CustomerReference(subscriber.UserKey);

        // Serialise concurrent attempts by the same shopper for the same plan, so the check below sees
        // whatever a racing request has just created rather than racing it to a second subscription.
        using var _ = await _enrolmentLock.AcquireAsync($"{customerReference}|{plan.Handle}", cancellationToken);

        try
        {
            var customer = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken);
            var existing = await FindSubscriptionsForPlanAsync(customer.Id, plan.Handle, cancellationToken);

            var live = existing.FirstOrDefault(subscription => SubscriptionStates.IsLive(subscription.State));
            if (live is not null)
            {
                _logger.LogInformation(
                    "Subscriber {CustomerReference} is already subscribed to {PlanHandle} (subscription {SubscriptionId}, state {State}).",
                    customerReference,
                    plan.Handle,
                    live.Id,
                    live.State);

                return new SubscribeResult(Map(live, plan), AlreadySubscribed: true);
            }

            // How many times this shopper has been on this plan before. Folding it into the write keeps
            // a later re-subscribe distinct from the original attempt instead of colliding with it.
            var generation = existing.Count;
            var request = new CreateMaxioSubscriptionRequest
            {
                Subscription = new CreateMaxioSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customer.Id,
                    Reference = _references.SubscriptionReference(customerReference, plan.Handle, generation),
                    PaymentCollectionMethod = NormalizeCollectionMethod(settings.PaymentCollectionMethod)
                },
                UniquenessToken = string.IsNullOrWhiteSpace(idempotencyKey)
                    ? _references.UniquenessToken(
                        "subscription",
                        customerReference,
                        plan.Handle,
                        generation.ToString(CultureInfo.InvariantCulture),
                        CurrentIdempotencyWindow(settings))
                    : _references.UniquenessToken("subscription", customerReference, idempotencyKey.Trim())
            };

            try
            {
                var created = await _client.CreateSubscriptionAsync(request, cancellationToken);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} on plan {PlanHandle} for {CustomerReference} in state {State}.",
                    created.Id,
                    plan.Handle,
                    customerReference,
                    created.State);

                return new SubscribeResult(Map(created, plan), AlreadySubscribed: false);
            }
            catch (MaxioApiException exception) when (exception.IsDuplicateSubmission)
            {
                // An identical write was already accepted. Maxio will not say what became of it, so ask.
                var settled = await FindSubscriptionsForPlanAsync(customer.Id, plan.Handle, cancellationToken);
                var winner = settled.FirstOrDefault(subscription => SubscriptionStates.IsLive(subscription.State));

                if (winner is not null)
                {
                    return new SubscribeResult(Map(winner, plan), AlreadySubscribed: true);
                }

                // Either a racing request has not settled yet, or a recent attempt was rejected and
                // Maxio is still deduplicating against it. Both clear within the idempotency window.
                throw new BillingConflictException(
                    $"A recent subscribe request for plan '{plan.Handle}' has not settled yet, so this one was not applied. " +
                    $"Retry in up to {settings.IdempotencyWindowSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
                    exception.Errors);
            }
        }
        catch (Exception exception)
        {
            throw Translate(exception, $"subscribe to plan '{plan.Handle}'");
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        EnsureConfigured();

        var customerReference = _references.CustomerReference(subscriber.UserKey);

        try
        {
            var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                // The shopper has never subscribed; that is not an error.
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

            return subscriptions
                .OrderByDescending(subscription => subscription.CreatedAt)
                .Select(subscription => Map(subscription, plan: null))
                .ToArray();
        }
        catch (Exception exception)
        {
            throw Translate(exception, "list subscriptions");
        }
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> LoadPlansAsync(
        string familyHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            // Products do not carry a currency; the site's primary currency applies to all of them.
            var site = await _client.GetSiteAsync(cancellationToken);
            var currency = site?.Currency ?? string.Empty;
            var products = await _client.ListProductsForFamilyAsync(familyHandle, cancellationToken);

            return products
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(product => Map(product, currency))
                .OrderBy(plan => plan.PriceInCents)
                .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (MaxioApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingValidationException(
                $"Maxio has no product family with handle '{familyHandle}'. Check Maxio:ProductFamilyHandle.",
                exception.Errors);
        }
        catch (Exception exception)
        {
            throw Translate(exception, "list subscription plans");
        }
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(
        string? planHandle,
        MaxioSettings settings,
        CancellationToken cancellationToken)
    {
        var requested = string.IsNullOrWhiteSpace(planHandle) ? settings.DefaultPlanHandle : planHandle;

        if (string.IsNullOrWhiteSpace(requested))
        {
            var available = await ListPlansAsync(cancellationToken);
            throw new BillingValidationException(
                "A plan handle is required. Choose one from GET /api/subscription-plans.",
                available.Select(plan => plan.Handle));
        }

        return await FindPlanAsync(requested, cancellationToken)
               ?? throw new SubscriptionPlanNotFoundException(requested.Trim());
    }

    /// <summary>
    /// Returns the Maxio customer for this shopper, creating it on first subscribe. Safe to call
    /// concurrently: a losing racer converges on the customer the winner created.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        Subscriber subscriber,
        string customerReference,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            // No uniqueness_token here on purpose. Maxio enforces customer references as unique for
            // the life of the site, which is a stronger and more precise guard than a token that only
            // deduplicates for 60 minutes: a duplicate create always fails, and the loser converges on
            // the winner below. A token would additionally block a legitimate retry after a rejected
            // attempt until its window expired.
            var request = new CreateMaxioCustomerRequest
            {
                Customer = new CreateMaxioCustomer
                {
                    FirstName = Fallback(subscriber.FirstName, "eShopOnWeb"),
                    LastName = Fallback(subscriber.LastName, "Shopper"),
                    Email = subscriber.Email,
                    Reference = customerReference
                }
            };

            var created = await _client.CreateCustomerAsync(request, cancellationToken);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id,
                customerReference);

            return created;
        }
        catch (MaxioApiException exception) when (exception.IsDuplicateSubmission || IsReferenceTaken(exception))
        {
            // Another request created this customer first. Re-read rather than fail.
            var winner = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw new BillingConflictException(
                "A concurrent request is still creating the billing customer for this account. Retry in a few seconds.",
                exception.Errors);
        }
        catch (Exception exception)
        {
            throw Translate(exception, "create the billing customer");
        }
    }

    private async Task<IReadOnlyList<MaxioSubscription>> FindSubscriptionsForPlanAsync(
        long customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Where(subscription => string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ToArray();
    }

    /// <summary>
    /// Index of the time window the current attempt falls into, folded into the subscription
    /// uniqueness token.
    /// </summary>
    /// <remarks>
    /// Unlike a customer reference, a subscription reference is not enforced as unique by Maxio, so a
    /// replayed write really would create a second subscription and the <c>uniqueness_token</c> is the
    /// only thing that prevents it. A token that never varies has a cost though: Maxio consumes it
    /// even for a rejected attempt, so a shopper who fixes whatever the rejection complained about
    /// would be locked out for the full 60 minutes of Maxio's window. Scoping the token to a short
    /// window keeps the protection where it matters — a double-click or a replayed request, which
    /// arrive milliseconds apart — while bounding that lockout to
    /// <see cref="MaxioSettings.IdempotencyWindowSeconds"/>. Requests that straddle a window boundary
    /// fall back to the in-process lock and the pre-flight read, which already cover them.
    /// </remarks>
    private string CurrentIdempotencyWindow(MaxioSettings settings)
    {
        var windowSeconds = Math.Max(1, settings.IdempotencyWindowSeconds);
        var window = _timeProvider.GetUtcNow().ToUnixTimeSeconds() / windowSeconds;

        return window.ToString(CultureInfo.InvariantCulture);
    }

    private MaxioSettings EnsureConfigured()
    {
        var settings = _settings.CurrentValue;
        if (settings.IsConfigured)
        {
            return settings;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            missing.Add($"{MaxioSettings.SectionName}:ApiKey");
        }

        if (string.IsNullOrWhiteSpace(settings.Subdomain) && string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            missing.Add($"{MaxioSettings.SectionName}:Subdomain (or {MaxioSettings.SectionName}:BaseUrl)");
        }

        if (string.IsNullOrWhiteSpace(settings.ProductFamilyHandle))
        {
            missing.Add($"{MaxioSettings.SectionName}:ProductFamilyHandle");
        }

        throw new BillingNotConfiguredException(
            $"Subscription billing is not configured. Missing configuration: {string.Join(", ", missing)}.");
    }

    /// <summary>Maps a transport level failure onto the application level billing error it represents.</summary>
    private static Exception Translate(Exception exception, string operation) => exception switch
    {
        BillingException billing => billing,

        MaxioApiException { StatusCode: HttpStatusCode.UnprocessableEntity } api =>
            new BillingValidationException(
                api.Errors.Count > 0
                    ? $"Maxio rejected the request to {operation}: {string.Join("; ", api.Errors)}"
                    : $"Maxio rejected the request to {operation}.",
                api.Errors),

        MaxioApiException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } api =>
            new BillingUnavailableException(
                $"Maxio rejected the configured API credentials while trying to {operation}.",
                api.Errors,
                api),

        MaxioApiException api =>
            new BillingUnavailableException(
                $"Maxio could not complete the request to {operation} ({(int)api.StatusCode}).",
                api.Errors,
                api),

        MaxioTransportException transport =>
            new BillingUnavailableException($"Maxio is unreachable, so eShopOnWeb could not {operation}.", null, transport),

        _ => exception
    };

    private static bool IsReferenceTaken(MaxioApiException exception) =>
        exception.StatusCode == HttpStatusCode.UnprocessableEntity &&
        exception.Errors.Any(error => error.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                                      error.Contains("taken", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Relationship Invoicing sites accept <c>remittance</c>, <c>automatic</c> and <c>prepaid</c>;
    /// blank means "leave it to the site default".
    /// </summary>
    private static string? NormalizeCollectionMethod(string? method) =>
        string.IsNullOrWhiteSpace(method) ? null : method.Trim().ToLowerInvariant();

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static SubscriptionPlan Map(MaxioProduct product, string currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        HasTrial = product.TrialInterval is > 0,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        SetupFeeInCents = product.InitialChargeInCents,
        RequiresPaymentMethod = product.RequireCreditCard
    };

    /// <summary>
    /// Projects a Maxio subscription. <paramref name="plan"/> supplies the currency for a freshly
    /// created subscription whose payload has not settled it yet; otherwise the subscription wins.
    /// </summary>
    private static CustomerSubscription Map(MaxioSubscription subscription, SubscriptionPlan? plan) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle ?? plan?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? plan?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? plan?.Currency ?? string.Empty : subscription.Currency!,
        Interval = subscription.Product?.Interval ?? plan?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? plan?.IntervalUnit ?? string.Empty,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? string.Empty,
        BalanceInCents = subscription.BalanceInCents,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference
    };
}
