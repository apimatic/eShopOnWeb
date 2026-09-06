using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// Maxio is the system of record: this class holds no local copy of the customer-to-subscription
/// mapping. The link is re-derivable at any time from the eShopOnWeb user name via
/// <see cref="MaxioReference"/>, which keeps the integration correct across restarts and across
/// instances, and keeps it working on the in-memory database where nothing is persisted.
/// </remarks>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan ProductFamilyCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SiteCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CustomerCacheDuration = TimeSpan.FromMinutes(30);

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioSettings> _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioSettings> settings,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var settings = GetConfiguredSettings();

        try
        {
            var familyId = await ResolveProductFamilyIdAsync(settings.ProductFamilyHandle!, cancellationToken);
            var products = await _client.ListProductsForFamilyAsync(familyId, cancellationToken);
            var currency = await ResolveSiteCurrencyAsync(cancellationToken);

            return products
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(product => MapPlan(product, currency, settings.ProductFamilyHandle!))
                .OrderBy(plan => plan.PriceInCents)
                .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "list subscription plans");
        }
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var settings = GetConfiguredSettings();

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new BillingRequestRejectedException("A plan handle is required to subscribe.");
        }

        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw new SubscriptionPlanNotFoundException(request.PlanHandle, settings.ProductFamilyHandle!);

        // This integration never captures a card, so a plan that demands a stored payment method
        // cannot be fulfilled here. Fail with an explanation rather than a provider-level 422.
        if (plan.RequiresPaymentMethod)
        {
            throw new BillingRequestRejectedException(
                $"Plan '{plan.Handle}' requires a stored payment method, which this integration does not capture.");
        }

        try
        {
            var customerReference = MaxioReference.ForCustomer(request.Subscriber.UserName);
            var customerId = await EnsureCustomerAsync(request.Subscriber, customerReference, cancellationToken);

            // Absent an explicit key, a subscriber holds at most one live subscription per plan;
            // that is what makes a double-click a no-op. An explicit key is the caller declaring
            // a distinct intent, so the per-plan guard is skipped and uniqueness of the
            // subscription reference alone decides.
            var hasExplicitKey = !string.IsNullOrWhiteSpace(request.IdempotencyKey);

            if (!hasExplicitKey)
            {
                var held = await FindLiveSubscriptionForPlanAsync(customerId, plan.Handle, cancellationToken);
                if (held is not null)
                {
                    _logger.LogInformation(
                        "Subscriber already holds a live subscription {SubscriptionId} to plan {PlanHandle}; returning it unchanged.",
                        held.Id,
                        plan.Handle);

                    return new SubscribeResult(MapSubscription(held, customerReference), alreadySubscribed: true);
                }
            }

            var subscriptionReference = MaxioReference.ForSubscription(
                customerReference,
                hasExplicitKey ? request.IdempotencyKey! : plan.Handle);

            return await CreateSubscriptionAsync(plan, customerId, customerReference, subscriptionReference, settings, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, $"subscribe to plan '{plan.Handle}'");
        }
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        GetConfiguredSettings();

        var customerReference = MaxioReference.ForCustomer(subscriber.UserName);

        try
        {
            var customerId = await FindCustomerIdAsync(customerReference, cancellationToken);
            if (customerId is null)
            {
                // No billing customer yet simply means the shopper has never subscribed.
                return Array.Empty<Subscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);

            return subscriptions
                .Select(subscription => MapSubscription(subscription, customerReference))
                .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "list subscriptions");
        }
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        SubscriptionPlan plan,
        long customerId,
        string customerReference,
        string subscriptionReference,
        MaxioSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _client.CreateSubscriptionAsync(
                new MaxioCreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerId = customerId,
                    Reference = subscriptionReference,
                    PaymentCollectionMethod = settings.PaymentCollectionMethod
                },
                cancellationToken);

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on plan {PlanHandle} for customer {CustomerId}.",
                created.Id,
                plan.Handle,
                customerId);

            return new SubscribeResult(MapSubscription(created, customerReference), alreadySubscribed: false);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            // A concurrent request already created this subscription. Maxio's uniqueness check on
            // the reference is what serialises the race; resolve to the winner's record.
            var existing = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);

            if (existing is null)
            {
                throw;
            }

            if (ParseState(existing.State).IsLive())
            {
                _logger.LogInformation(
                    "Subscribe request collapsed onto existing subscription {SubscriptionId} for plan {PlanHandle}.",
                    existing.Id,
                    plan.Handle);

                return new SubscribeResult(MapSubscription(existing, customerReference), alreadySubscribed: true);
            }

            throw new SubscriptionConflictException(
                $"A previous subscription to plan '{plan.Handle}' already used this idempotency key and is no longer active " +
                "(state: " + (existing.State ?? "unknown") + "). Supply a distinct idempotencyKey to subscribe again.");
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForPlanAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            ParseState(subscription.State).IsLive());
    }

    private async Task<long> EnsureCustomerAsync(Subscriber subscriber, string customerReference, CancellationToken cancellationToken)
    {
        var existingId = await FindCustomerIdAsync(customerReference, cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var (firstName, lastName) = DeriveName(subscriber);

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

            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, customerReference);

            CacheCustomerId(customerReference, created.Id);
            return created.Id;
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateReference)
        {
            // Another concurrent request created the customer first. Adopt it.
            var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            CacheCustomerId(customerReference, existing.Id);
            return existing.Id;
        }
    }

    private async Task<long?> FindCustomerIdAsync(string customerReference, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<long>(CustomerCacheKey(customerReference), out var cachedId) && cachedId != 0)
        {
            return cachedId;
        }

        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            // Deliberately not cached: the absence is what the subscribe path is about to change.
            return null;
        }

        CacheCustomerId(customerReference, customer.Id);
        return customer.Id;
    }

    private void CacheCustomerId(string customerReference, long customerId) =>
        _cache.Set(CustomerCacheKey(customerReference), customerId, CustomerCacheDuration);

    private static string CustomerCacheKey(string customerReference) => $"maxio:customer-id:{customerReference}";

    private async Task<long> ResolveProductFamilyIdAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio:product-family-id:{productFamilyHandle}";

        if (_cache.TryGetValue<long>(cacheKey, out var cachedId) && cachedId != 0)
        {
            return cachedId;
        }

        // Handles are the stable identifier; numeric ids are reassigned when a catalog is
        // re-seeded, so the id is always looked up rather than configured.
        var families = await _client.ListProductFamiliesAsync(cancellationToken);

        var family = families.FirstOrDefault(f =>
            string.Equals(f.Handle, productFamilyHandle, StringComparison.OrdinalIgnoreCase) && f.ArchivedAt is null);

        if (family is null)
        {
            throw new BillingProviderException(
                $"No product family with handle '{productFamilyHandle}' exists on the configured Maxio site. " +
                $"Check {MaxioSettings.ConfigurationSectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}.");
        }

        _cache.Set(cacheKey, family.Id, ProductFamilyCacheDuration);
        return family.Id;
    }

    private async Task<string?> ResolveSiteCurrencyAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "maxio:site-currency";

        if (_cache.TryGetValue<string>(cacheKey, out var cached))
        {
            return cached;
        }

        // Products do not carry a currency; the site does. Failing to read it must not take the
        // plan listing down, so a failure here degrades to an unlabelled price.
        try
        {
            var site = await _client.GetSiteAsync(cancellationToken);
            _cache.Set(cacheKey, site?.Currency, SiteCacheDuration);
            return site?.Currency;
        }
        catch (MaxioApiException ex)
        {
            _logger.LogWarning(ex, "Could not read the Maxio site currency; plan prices will be returned without one.");
            return null;
        }
    }

    private MaxioSettings GetConfiguredSettings()
    {
        var settings = _settings.CurrentValue;
        var missing = settings.GetMissingSettings();

        if (missing.Count > 0)
        {
            throw new BillingNotConfiguredException(missing);
        }

        return settings;
    }

    /// <summary>
    /// Maxio requires a first and last name on every customer. eShopOnWeb only knows an email
    /// address, so a name is derived from its local part, tagged with the source application so
    /// the record is recognisable in the Maxio UI.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(Subscriber subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) || !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (Fallback(subscriber.FirstName, "eShopOnWeb"), Fallback(subscriber.LastName, "User"));
        }

        var source = subscriber.Email;
        var atIndex = source.IndexOf('@');
        var localPart = atIndex > 0 ? source.Substring(0, atIndex) : source;

        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleCase)
            .ToArray();

        return tokens.Length switch
        {
            0 => ("eShopOnWeb", "User"),
            1 => (tokens[0], "eShopOnWeb"),
            _ => (tokens[0], string.Join(" ", tokens.Skip(1)))
        };

        static string Fallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
    }

    private static string TitleCase(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value.Substring(1);

    private static SubscriptionPlan MapPlan(MaxioProduct product, string? currency, string productFamilyHandle) => new()
    {
        Id = product.Id,
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? productFamilyHandle
    };

    private static Subscription MapSubscription(MaxioSubscription subscription, string customerReference) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = ParseState(subscription.State),
        RawState = subscription.State,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        ExpiresAt = subscription.ExpiresAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference ?? customerReference
    };

    private static SubscriptionState ParseState(string? state) => state?.ToLowerInvariant() switch
    {
        "pending" => SubscriptionState.Pending,
        "trialing" => SubscriptionState.Trialing,
        "assessing" => SubscriptionState.Assessing,
        "active" => SubscriptionState.Active,
        "paused" => SubscriptionState.Paused,
        "past_due" => SubscriptionState.PastDue,
        "soft_failure" => SubscriptionState.SoftFailure,
        "unpaid" => SubscriptionState.Unpaid,
        "canceled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "failed_to_create" => SubscriptionState.FailedToCreate,
        "on_hold" => SubscriptionState.OnHold,
        "suspended" => SubscriptionState.Suspended,
        "trial_ended" => SubscriptionState.TrialEnded,
        _ => SubscriptionState.Unknown
    };

    private BillingException Translate(MaxioApiException exception, string operation)
    {
        switch (exception.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                // Never echo provider detail here: it can restate the credential that was sent.
                _logger.LogError(exception, "Maxio rejected our credentials while trying to {Operation}.", operation);
                return new BillingProviderException(
                    $"The billing provider rejected this deployment's credentials. Check {MaxioSettings.ConfigurationSectionName}:{nameof(MaxioSettings.ApiKey)}.");

            case HttpStatusCode.BadRequest:
            case HttpStatusCode.UnprocessableEntity:
                _logger.LogWarning(exception, "Maxio refused the request to {Operation}.", operation);
                return new BillingRequestRejectedException(
                    $"The billing provider refused the request to {operation}.",
                    exception.Errors);

            default:
                _logger.LogError(exception, "Maxio failed while trying to {Operation}.", operation);
                return new BillingProviderException(
                    $"The billing provider failed while trying to {operation}.",
                    exception,
                    exception.Errors);
        }
    }
}
