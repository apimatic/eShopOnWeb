using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Implements the subscription capability against Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// Maxio is the system of record: eShopOnWeb stores no local copy of customers, plans or
/// subscriptions. The join between the two systems is the eShopOnWeb-owned value written to the
/// Maxio customer's <c>reference</c> field, which Maxio constrains to be unique per site.
/// </remarks>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const string PlanCacheKey = "maxio:plans";
    private const string SiteCacheKey = "maxio:site";

    private readonly IMaxioApiClient _client;
    private readonly MaxioCatalogCache _catalog;
    private readonly KeyedAsyncLock _subscriberLocks;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        MaxioCatalogCache catalog,
        KeyedAsyncLock subscriberLocks,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _catalog = catalog;
        _subscriberLocks = subscriberLocks;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetPlansAsync(forceReload: false, cancellationToken).ConfigureAwait(false);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "list subscription plans");
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(
        Subscriber subscriber,
        SubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ArgumentNullException.ThrowIfNull(request);

        var planHandle = FirstNonBlank(request.PlanHandle, _settings.DefaultPlanHandle);
        if (planHandle is null)
        {
            throw new SubscriptionPlanRequiredException(
                "A plan handle is required. Supply 'planHandle' on the request, or configure " +
                $"'{MaxioSettings.SectionName}:{nameof(MaxioSettings.DefaultPlanHandle)}'.");
        }

        try
        {
            var plan = await ResolvePlanAsync(planHandle, cancellationToken).ConfigureAwait(false);

            // Serialize per subscriber so two simultaneous clicks cannot both observe "not
            // subscribed yet". Across nodes the same outcome is guaranteed by the unique customer
            // reference and by the duplicate-prevention token below.
            using (await _subscriberLocks.AcquireAsync(subscriber.CustomerReference, cancellationToken)
                       .ConfigureAwait(false))
            {
                var customer = await EnsureCustomerAsync(subscriber, request, cancellationToken)
                    .ConfigureAwait(false);

                var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    _logger.LogInformation(
                        "Subscriber {CustomerReference} is already on plan {PlanHandle} via subscription {SubscriptionId}; returning it unchanged.",
                        subscriber.CustomerReference, plan.Handle, existing.Id);

                    return new SubscribeResult(MapSubscription(existing, customer), AlreadySubscribed: true);
                }

                var created = await CreateSubscriptionAsync(subscriber, customer, plan, request, cancellationToken)
                    .ConfigureAwait(false);

                return created;
            }
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, $"subscribe {subscriber.CustomerReference} to plan '{planHandle}'");
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        try
        {
            // A read must never create anything: an account that has never subscribed simply has
            // no Maxio customer yet.
            var customer = await _client
                .FindCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken)
                .ConfigureAwait(false);

            if (customer is null)
            {
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client
                .ListCustomerSubscriptionsAsync(customer.Id, cancellationToken)
                .ConfigureAwait(false);

            return subscriptions
                .Select(s => MapSubscription(s, customer))
                .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, $"list subscriptions for {subscriber.CustomerReference}");
        }
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        Subscriber subscriber,
        MaxioCustomer customer,
        SubscriptionPlan plan,
        SubscribeRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionAttributes
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = _settings.PaymentCollectionMethod
            },
            UniquenessToken = BuildUniquenessToken(subscriber.CustomerReference, plan.Handle, request.IdempotencyKey)
        };

        try
        {
            var subscription = await _client.CreateSubscriptionAsync(payload, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on plan {PlanHandle} for {CustomerReference} (customer {CustomerId}), state {State}.",
                subscription.Id, plan.Handle, subscriber.CustomerReference, customer.Id, subscription.State);

            return new SubscribeResult(MapSubscription(subscription, customer), AlreadySubscribed: false);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateSubmission)
        {
            // Maxio saw this exact token inside its 60-minute window, so the enrollment was already
            // submitted. Read back the authoritative state rather than guessing.
            _logger.LogInformation(
                "Maxio rejected a replayed enrollment token for {CustomerReference} on plan {PlanHandle}; reading back the existing subscription.",
                subscriber.CustomerReference, plan.Handle);

            var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return new SubscribeResult(MapSubscription(existing, customer), AlreadySubscribed: true);
            }

            throw new BillingProviderException(
                "An identical subscribe request is already in flight at Maxio and its outcome is not yet " +
                "visible. Retry in a moment, or use a different idempotency key.",
                upstreamStatusCode: (int)ex.StatusCode,
                providerErrors: ex.Errors,
                innerException: ex);
        }
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        Subscriber subscriber,
        SubscribeRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _client
            .FindCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var payload = new CreateCustomerRequest
        {
            Customer = new CreateCustomerAttributes
            {
                FirstName = FirstNonBlank(request.FirstName, subscriber.FirstName)!,
                LastName = FirstNonBlank(request.LastName, subscriber.LastName)!,
                Email = subscriber.Email,
                Reference = subscriber.CustomerReference
            },
            // A fresh token per attempt: it makes a transport-level retry of *this* create safe,
            // without masking a genuine validation failure on a later, deliberate retry.
            UniquenessToken = Guid.NewGuid().ToString("N")
        };

        try
        {
            var created = await _client.CreateCustomerAsync(payload, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for {CustomerReference}.",
                created.Id, subscriber.CustomerReference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateCustomerReference)
        {
            // Another node (or another request on this one) won the race. Maxio's uniqueness
            // constraint on reference is what makes that safe: read the winner and carry on.
            var winner = await _client
                .FindCustomerByReferenceAsync(subscriber.CustomerReference, cancellationToken)
                .ConfigureAwait(false);

            if (winner is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerId} for {CustomerReference} was created concurrently; reusing it.",
                    winner.Id, subscriber.CustomerReference);

                return winner;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(
        long customerId,
        string planHandle,
        CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken)
            .ConfigureAwait(false);

        return subscriptions
            .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .Where(s => SubscriptionStates.IsLive(s.State))
            .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plans = await GetPlansAsync(forceReload: false, cancellationToken).ConfigureAwait(false);
        var plan = Match(plans, planHandle);

        if (plan is null)
        {
            // The catalog may simply be stale in cache; pay for one reload before rejecting.
            plans = await GetPlansAsync(forceReload: true, cancellationToken).ConfigureAwait(false);
            plan = Match(plans, planHandle);
        }

        return plan ?? throw new SubscriptionPlanNotFoundException(planHandle);

        static SubscriptionPlan? Match(IReadOnlyList<SubscriptionPlan> plans, string handle) =>
            plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(
        bool forceReload,
        CancellationToken cancellationToken)
    {
        if (forceReload)
        {
            _catalog.Invalidate(PlanCacheKey);
        }

        var ttl = TimeSpan.FromSeconds(Math.Max(0, _settings.CatalogCacheSeconds));

        var plans = await _catalog.GetOrLoadAsync<IReadOnlyList<SubscriptionPlan>>(
            PlanCacheKey,
            ttl,
            LoadPlansAsync,
            cancellationToken).ConfigureAwait(false);

        return plans;
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> LoadPlansAsync(CancellationToken cancellationToken)
    {
        var currency = await GetSiteCurrencyAsync(cancellationToken).ConfigureAwait(false);

        var products = await _client
            .ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken)
            .ConfigureAwait(false);

        var plans = products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle!,
                ProviderPlanId = p.Id,
                Name = string.IsNullOrWhiteSpace(p.Name) ? p.Handle! : p.Name!,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Currency = currency,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit ?? string.Empty,
                RequiresPaymentMethod = p.RequireCreditCard,
                ProductFamilyHandle = p.ProductFamily?.Handle ?? _settings.ProductFamilyHandle
            })
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Loaded {PlanCount} subscription plan(s) from Maxio product family '{ProductFamilyHandle}'.",
            plans.Count, _settings.ProductFamilyHandle);

        return plans;
    }

    /// <summary>
    /// Maxio product payloads carry an amount but not a currency, so take the trading currency from
    /// the site record. Cached alongside the catalog; a failure here must not sink a plan listing.
    /// </summary>
    private async Task<string> GetSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ttl = TimeSpan.FromSeconds(Math.Max(0, _settings.CatalogCacheSeconds));
            var site = await _catalog.GetOrLoadAsync(
                SiteCacheKey,
                ttl,
                async ct => await _client.GetSiteAsync(ct).ConfigureAwait(false) ?? new MaxioSite(),
                cancellationToken).ConfigureAwait(false);

            return site.Currency ?? string.Empty;
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning(ex, "Could not read the Maxio site currency; plan prices will be reported without one.");
            return string.Empty;
        }
    }

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, MaxioCustomer? customer) =>
        new()
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = subscription.Currency ?? string.Empty,
            BalanceInCents = subscription.BalanceInCents,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt,
            CustomerId = subscription.Customer?.Id ?? customer?.Id ?? 0,
            CustomerReference = subscription.Customer?.Reference ?? customer?.Reference
        };

    /// <summary>
    /// Builds the value sent as Maxio's <c>uniqueness_token</c>.
    /// </summary>
    /// <remarks>
    /// When the caller supplies an idempotency key, the token is derived deterministically from
    /// that key together with the subscriber and plan, so a retry of the same logical request is
    /// recognised as a duplicate while an unrelated caller reusing the same key cannot collide with
    /// it. Without a caller key, a fresh random token is used: it still makes a transport-level
    /// retry of this attempt safe, and the per-subscriber lock plus the pre-flight check cover the
    /// double-click case.
    /// </remarks>
    internal static string BuildUniquenessToken(string customerReference, string planHandle, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Guid.NewGuid().ToString("N");
        }

        var material = string.Join(
            '|',
            "eshoponweb.subscribe.v1",
            customerReference,
            planHandle.ToLowerInvariant(),
            idempotencyKey.Trim());

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    /// <summary>
    /// Converts a transport-level Maxio failure into the layer-neutral exception the API surface
    /// knows how to map onto a status code.
    /// </summary>
    private BillingProviderException Translate(MaxioApiException exception, string operation)
    {
        _logger.LogError(
            exception,
            "Maxio call failed while attempting to {Operation}. Status {StatusCode}.",
            operation,
            (int)exception.StatusCode);

        var detail = exception.Errors.Count > 0
            ? string.Join("; ", exception.Errors)
            : exception.StatusCode.ToString();

        return new BillingProviderException(
            $"The billing provider could not {operation}: {detail}",
            upstreamStatusCode: (int)exception.StatusCode,
            providerErrors: exception.Errors,
            innerException: exception);
    }
}
