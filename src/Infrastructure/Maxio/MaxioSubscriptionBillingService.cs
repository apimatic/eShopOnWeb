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
/// Maxio Advanced Billing implementation of <see cref="ISubscriptionBillingService"/>.
/// </summary>
/// <remarks>
/// Maxio is the system of record: this service holds no local copy of customers, plans or
/// subscriptions, so the integration stays correct across app restarts and is unaffected by
/// eShopOnWeb running on the in-memory database. The link between an eShopOnWeb user and their
/// Maxio customer is the customer <c>reference</c>, derived from the login name.
/// </remarks>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string SiteCacheKey = "maxio:site";
    private const string PlansCacheKeyPrefix = "maxio:plans:";

    /// <summary>Upper bound on the reference suffixes tried when a shopper re-subscribes to a plan.</summary>
    private const int MaxReferenceAttempts = 100;

    private readonly IMaxioApiClient _client;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly IMemoryCache _cache;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IOptionsMonitor<MaxioOptions> options,
        IMemoryCache cache,
        KeyedAsyncLock subscriberLock,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _subscriberLock = subscriberLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var options = GetValidatedOptions();
        var familyHandle = options.ProductFamilyHandle!;
        var cacheKey = PlansCacheKeyPrefix + familyHandle;

        if (options.CatalogCacheSeconds > 0 && _cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var currency = await ResolveSiteCurrencyAsync(options, cancellationToken);

        IReadOnlyList<MaxioProduct> products;
        try
        {
            products = await _client.ListProductsInFamilyAsync(familyHandle, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionBillingException(
                BillingErrorKind.Configuration,
                $"No Maxio product family has the handle '{familyHandle}'. Check '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}'.",
                ex.Errors,
                ex);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "list subscription plans");
        }

        var plans = products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => ToPlan(p, currency))
            .ToArray();

        if (options.CatalogCacheSeconds > 0)
        {
            _cache.Set(cacheKey, (IReadOnlyList<SubscriptionPlan>)plans, TimeSpan.FromSeconds(options.CatalogCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var options = GetValidatedOptions();
        var resolvedPlanHandle = ResolvePlanHandle(planHandle, options);
        var customerReference = BuildCustomerReference(subscriber, options);

        // The plan is validated before anything is written, so an unknown or out-of-catalog handle
        // never leaves a stray customer behind in Maxio.
        var plan = await ResolvePlanAsync(resolvedPlanHandle, options, cancellationToken);

        // Serialise concurrent subscribe attempts for this shopper so the read-then-create below is
        // not interleaved with itself. See KeyedAsyncLock for the cross-instance caveat.
        using (await _subscriberLock.AcquireAsync(customerReference, cancellationToken))
        {
            var customer = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken);
            var existingSubscriptions = await ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);

            var alreadySubscribed = FindLiveSubscriptionForPlan(existingSubscriptions, plan.Handle!);
            if (alreadySubscribed is not null)
            {
                _logger.LogInformation(
                    "Subscribe request for {CustomerReference} to plan {PlanHandle} matched existing subscription {SubscriptionId} in state {State}; not creating another.",
                    customerReference,
                    plan.Handle,
                    alreadySubscribed.Id,
                    alreadySubscribed.State);

                return SubscribeResult.AlreadySubscribed(ToSubscription(alreadySubscribed, customerReference));
            }

            var subscriptionReference = BuildSubscriptionReference(customerReference, plan.Handle!, existingSubscriptions);

            var request = new CreateSubscription
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = options.PaymentCollectionMethod,
                Reference = subscriptionReference
            };

            try
            {
                var created = await _client.CreateSubscriptionAsync(request, cancellationToken);

                _logger.LogInformation(
                    "Created Maxio subscription {SubscriptionId} for {CustomerReference} on plan {PlanHandle} (state {State}).",
                    created.Id,
                    customerReference,
                    plan.Handle,
                    created.State);

                return SubscribeResult.NewlyCreated(ToSubscription(created, customerReference));
            }
            catch (MaxioApiException ex) when (IsDuplicateReference(ex))
            {
                // Another instance created this subscription between our check and our write. Maxio's
                // uniqueness constraint on the subscription reference is what caught it.
                var raced = FindLiveSubscriptionForPlan(
                    await ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken),
                    plan.Handle!);

                if (raced is not null)
                {
                    _logger.LogInformation(
                        "Concurrent subscribe for {CustomerReference} on plan {PlanHandle} resolved to existing subscription {SubscriptionId}.",
                        customerReference,
                        plan.Handle,
                        raced.Id);

                    return SubscribeResult.AlreadySubscribed(ToSubscription(raced, customerReference));
                }

                throw Translate(ex, $"subscribe to plan '{plan.Handle}'");
            }
            catch (MaxioApiException ex)
            {
                throw Translate(ex, $"subscribe to plan '{plan.Handle}'");
            }
        }
    }

    public async Task<IReadOnlyList<SubscriberSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var options = GetValidatedOptions();
        var customerReference = BuildCustomerReference(subscriber, options);

        MaxioCustomer? customer;
        try
        {
            customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "look up the billing customer");
        }

        if (customer is null)
        {
            // The shopper has never subscribed, so no Maxio customer exists yet. Not an error.
            return Array.Empty<SubscriberSubscription>();
        }

        var subscriptions = await ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => ToSubscription(s, customerReference))
            .ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriberIdentity subscriber,
        string customerReference,
        CancellationToken cancellationToken)
    {
        MaxioCustomer? customer;
        try
        {
            customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "look up the billing customer");
        }

        if (customer is not null)
        {
            return customer;
        }

        var (firstName, lastName) = ResolveName(subscriber);

        try
        {
            var created = await _client.CreateCustomerAsync(
                new CreateCustomer
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
        catch (MaxioApiException ex) when (IsDuplicateReference(ex))
        {
            // Another instance created the customer first. Maxio guarantees the reference is unique,
            // so re-reading it yields the one and only customer for this shopper.
            var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw Translate(ex, "create the billing customer");
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "create the billing customer");
        }
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, "list the shopper's subscriptions");
        }
    }

    /// <summary>
    /// Reads the requested plan and confirms it is subscribable and inside the configured catalog.
    /// </summary>
    private async Task<MaxioProduct> ResolvePlanAsync(
        string planHandle,
        MaxioOptions options,
        CancellationToken cancellationToken)
    {
        MaxioProduct? product;
        try
        {
            product = await _client.ReadProductByHandleAsync(planHandle, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex, $"read plan '{planHandle}'");
        }

        if (product is null || string.IsNullOrWhiteSpace(product.Handle))
        {
            throw new SubscriptionBillingException(
                BillingErrorKind.NotFound,
                $"No subscription plan with handle '{planHandle}' exists.");
        }

        // A product handle is unique site-wide, so without this check a shopper could subscribe to a
        // plan from a product family this deployment is not selling.
        if (!string.Equals(product.ProductFamily?.Handle, options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionBillingException(
                BillingErrorKind.NotFound,
                $"No subscription plan with handle '{planHandle}' exists in product family '{options.ProductFamilyHandle}'.");
        }

        if (product.ArchivedAt is not null)
        {
            throw new SubscriptionBillingException(
                BillingErrorKind.Validation,
                $"Subscription plan '{planHandle}' has been archived and can no longer be subscribed to.");
        }

        return product;
    }

    private static string ResolvePlanHandle(string? requested, MaxioOptions options)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultPlanHandle))
        {
            return options.DefaultPlanHandle.Trim();
        }

        throw new SubscriptionBillingException(
            BillingErrorKind.Validation,
            "A plan handle is required.",
            new[]
            {
                $"Supply 'planHandle', or configure '{MaxioOptions.SectionName}:{nameof(MaxioOptions.DefaultPlanHandle)}' to make one the default."
            });
    }

    private static MaxioSubscription? FindLiveSubscriptionForPlan(IReadOnlyList<MaxioSubscription> subscriptions, string planHandle) =>
        subscriptions
            .Where(s => SubscriptionStates.IsLive(s.State) &&
                        string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();

    /// <summary>
    /// Builds the reference stored against the subscription. Maxio enforces uniqueness on it, which
    /// makes it a server-side backstop against duplicate enrollments — including across app
    /// instances, where the in-process lock does not reach.
    /// </summary>
    /// <remarks>
    /// The base form is deterministic per (customer, plan). Because a shopper may legitimately
    /// re-subscribe after cancelling, and the old subscription keeps its reference forever, a
    /// numeric suffix is added for each subsequent enrollment in the same plan.
    /// </remarks>
    private static string BuildSubscriptionReference(
        string customerReference,
        string planHandle,
        IReadOnlyList<MaxioSubscription> existingSubscriptions)
    {
        var baseReference = $"{customerReference}:{planHandle}";

        var taken = new HashSet<string>(
            existingSubscriptions.Select(s => s.Reference).Where(r => !string.IsNullOrEmpty(r))!,
            StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(baseReference))
        {
            return baseReference;
        }

        for (var attempt = 2; attempt <= MaxReferenceAttempts; attempt++)
        {
            var candidate = $"{baseReference}:{attempt.ToString(CultureInfo.InvariantCulture)}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new SubscriptionBillingException(
            BillingErrorKind.Validation,
            $"This shopper has already been enrolled in plan '{planHandle}' {MaxReferenceAttempts} times.");
    }

    /// <summary>
    /// Derives the Maxio customer reference from the eShopOnWeb login name. The login name is
    /// unique per user and stable across restarts, so the same shopper always maps to the same
    /// Maxio customer even when eShopOnWeb runs on the in-memory database.
    /// </summary>
    private static string BuildCustomerReference(SubscriberIdentity subscriber, MaxioOptions options)
    {
        var prefix = string.IsNullOrWhiteSpace(options.CustomerReferencePrefix)
            ? string.Empty
            : options.CustomerReferencePrefix.Trim() + "-";

        return prefix + subscriber.UserName.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Maxio requires a first and last name on every customer, but eShopOnWeb's identity model
    /// stores neither. Uses what the caller supplied and otherwise derives something recognisable
    /// from the email address.
    /// </summary>
    private static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        var localPart = subscriber.Email.Split('@')[0];
        var domain = subscriber.Email.Contains('@') ? subscriber.Email.Split('@')[1] : string.Empty;

        var words = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = subscriber.FirstName;
        var lastName = subscriber.LastName;

        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = words.Length > 0 ? Titleise(words[0]) : localPart;
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            lastName = words.Length > 1
                ? Titleise(words[^1])
                : Titleise(domain.Split('.')[0]);
        }

        // Maxio rejects a blank first or last name; fall back to the raw address rather than fail.
        return (
            string.IsNullOrWhiteSpace(firstName) ? subscriber.Email : firstName,
            string.IsNullOrWhiteSpace(lastName) ? subscriber.Email : lastName);
    }

    private static string Titleise(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private async Task<string?> ResolveSiteCurrencyAsync(MaxioOptions options, CancellationToken cancellationToken)
    {
        if (options.CatalogCacheSeconds > 0 && _cache.TryGetValue(SiteCacheKey, out string? cached))
        {
            return cached;
        }

        string? currency = null;
        try
        {
            var site = await _client.ReadSiteAsync(cancellationToken);
            currency = site?.Currency;
        }
        catch (MaxioApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw Translate(ex, "read the Maxio site");
        }
        catch (MaxioApiException ex)
        {
            // Currency is presentational: a plan list is still useful without it.
            _logger.LogWarning(ex, "Could not read the Maxio site; plan prices will be reported without a currency.");
        }

        if (options.CatalogCacheSeconds > 0)
        {
            _cache.Set(SiteCacheKey, currency, TimeSpan.FromSeconds(options.CatalogCacheSeconds));
        }

        return currency;
    }

    private MaxioOptions GetValidatedOptions()
    {
        var options = _options.CurrentValue;
        var failures = options.Validate();

        if (failures.Count > 0)
        {
            throw new SubscriptionBillingException(
                BillingErrorKind.Configuration,
                "The Maxio billing integration is not configured.",
                failures);
        }

        return options;
    }

    private static bool IsSiteClearing(MaxioApiException ex) =>
        ex.Errors.Any(e => e.Contains("Site data clearing", StringComparison.OrdinalIgnoreCase));

    private static bool IsDuplicateReference(MaxioApiException ex) =>
        ex.StatusCode == HttpStatusCode.UnprocessableEntity &&
        ex.Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
                           e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Maps a Maxio transport failure onto a provider-neutral one the API layer can turn into a
    /// status code. Maxio rejecting <em>our</em> credentials is our configuration problem, never the
    /// shopper's request.
    /// </summary>
    private SubscriptionBillingException Translate(MaxioApiException ex, string operation)
    {
        var kind = ex.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => BillingErrorKind.Configuration,
            HttpStatusCode.NotFound => BillingErrorKind.NotFound,

            // A site being cleared answers every read with 422 "Site data clearing is in progress.
            // Please try later." The spec calls clearing asynchronous with "a delay before the site
            // data is fully deleted" (clearSite), so this is a wait-and-retry condition, not the
            // caller sending something invalid.
            HttpStatusCode.UnprocessableEntity when IsSiteClearing(ex) => BillingErrorKind.Unavailable,

            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => BillingErrorKind.Validation,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => BillingErrorKind.Unavailable,
            HttpStatusCode.TooManyRequests => BillingErrorKind.Unavailable,
            >= HttpStatusCode.InternalServerError => BillingErrorKind.Unavailable,
            _ => BillingErrorKind.Unexpected
        };

        if (kind == BillingErrorKind.Configuration)
        {
            _logger.LogError(
                ex,
                "Maxio rejected the API credentials while trying to {Operation}. Check the '{Section}' configuration.",
                operation,
                MaxioOptions.SectionName);

            return new SubscriptionBillingException(
                kind,
                "The billing system rejected this application's credentials.",
                Array.Empty<string>(),
                ex);
        }

        return new SubscriptionBillingException(kind, $"Could not {operation}.", ex.Errors, ex);
    }

    private static SubscriptionPlan ToPlan(MaxioProduct product, string? currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        UpdatedAt = product.UpdatedAt
    };

    private static SubscriberSubscription ToSubscription(MaxioSubscription subscription, string customerReference) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        TrialEndsAt = subscription.TrialEndedAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        BalanceInCents = subscription.BalanceInCents,
        Reference = subscription.Reference,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference ?? customerReference
    };
}
