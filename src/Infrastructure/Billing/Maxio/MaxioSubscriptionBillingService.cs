using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
///
/// Maxio is the system of record: this service keeps no local copy of the shopper-to-customer
/// or shopper-to-subscription mapping. Instead every shopper gets a deterministic customer
/// <c>reference</c> derived from their eShopOnWeb identity, and lookups go through it. That keeps
/// the integration correct across restarts even when eShopOnWeb runs on the in-memory database.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const string PlanCacheKeyPrefix = "maxio:plans:";
    private const string SiteCacheKey = "maxio:site";

    private readonly MaxioApiClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly SubscriberKeyedLock _enrollmentLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(MaxioApiClient client, IOptions<MaxioOptions> options, IMemoryCache cache,
        SubscriberKeyedLock enrollmentLock, ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _enrollmentLock = enrollmentLock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        _options.EnsureValid();

        var familyHandle = _options.ProductFamilyHandle!.Trim();
        var cacheKey = PlanCacheKeyPrefix + familyHandle;

        if (_options.PlanCacheSeconds > 0 && _cache.TryGetValue<IReadOnlyList<SubscriptionPlan>>(cacheKey, out var cached)
            && cached is not null)
        {
            return cached;
        }

        // The product family can be addressed by handle, which is stable across re-seeds; its
        // numeric id is not.
        var url = $"product_families/handle:{Uri.EscapeDataString(familyHandle)}/products.json?per_page=200";
        var envelopes = await _client.GetOrDefaultAsync<List<MaxioProductEnvelope>>(url, cancellationToken);
        var currency = (await ReadSiteAsync(cancellationToken))?.Currency;

        if (envelopes is null)
        {
            throw new BillingConfigurationException(
                $"Maxio has no product family with handle '{familyHandle}'. Check {MaxioOptions.SectionName}:ProductFamilyHandle.");
        }

        var plans = envelopes
            .Select(envelope => envelope.Product)
            .Where(product => product is not null && product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => MapPlan(product!, currency))
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_options.PlanCacheSeconds > 0)
        {
            _cache.Set(cacheKey, (IReadOnlyList<SubscriptionPlan>)plans,
                TimeSpan.FromSeconds(_options.PlanCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscriptionPlan?> FindPlanAsync(string planHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var plans = await ListPlansAsync(cancellationToken);
        return plans.FirstOrDefault(plan => string.Equals(plan.Handle, planHandle.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var plan = await FindPlanAsync(planHandle, cancellationToken)
                   ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customerReference = BuildCustomerReference(subscriber);

        // One enrollment at a time per shopper: the existence check below is only meaningful if
        // a concurrent request cannot slip a create in between the read and the write.
        using var _ = await _enrollmentLock.AcquireAsync(customerReference, cancellationToken);

        var (customer, customerAlreadyExisted) = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken);
        var existing = await ReadCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        var live = existing.FirstOrDefault(subscription =>
            MatchesPlan(subscription, plan.Handle, customerReference) && SubscriptionStates.IsLive(subscription.State));

        if (live is not null)
        {
            _logger.LogInformation(
                "Shopper {CustomerReference} is already subscribed to {PlanHandle} (subscription {SubscriptionId}, state {State}); returning the existing enrollment.",
                customerReference, plan.Handle, live.Id, live.State);

            return new SubscriptionEnrollment
            {
                Subscription = MapSubscription(live, plan),
                Customer = customer,
                AlreadyEnrolled = true,
                CustomerAlreadyExisted = customerAlreadyExisted
            };
        }

        var subscriptionReference = BuildSubscriptionReference(customerReference, plan.Handle, existing);
        var site = await ReadSiteAsync(cancellationToken);

        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = plan.Handle,
                CustomerId = customer.Id,
                Reference = subscriptionReference,
                PaymentCollectionMethod = ResolvePaymentCollectionMethod(plan, site)
            },
            UniquenessToken = BuildUniquenessToken("subscription", subscriptionReference)
        };

        try
        {
            return await CreateSubscriptionAsync(request, plan, customer, customerAlreadyExisted, site,
                customerReference, cancellationToken);
        }
        catch (BillingApiException ex) when (ex.StatusCode == (int)HttpStatusCode.Conflict)
        {
            // Maxio rejected the duplicate-prevention token, so an identical create reached it
            // within the last hour - a client retry, a transport retry, or another instance.
            _logger.LogInformation(ex,
                "Maxio reported a duplicate create for {Reference}; checking whether it produced a subscription.",
                subscriptionReference);

            var reread = await ReadCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var duplicate = reread.FirstOrDefault(subscription =>
                                string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal))
                            ?? reread.FirstOrDefault(subscription =>
                                MatchesPlan(subscription, plan.Handle, customerReference) &&
                                SubscriptionStates.IsLive(subscription.State));

            if (duplicate is not null)
            {
                return new SubscriptionEnrollment
                {
                    Subscription = MapSubscription(duplicate, plan, site),
                    Customer = customer,
                    AlreadyEnrolled = true,
                    CustomerAlreadyExisted = customerAlreadyExisted
                };
            }

            // The earlier attempt was rejected without creating anything - a failed signup rather
            // than a duplicate. Retrying under a fresh token is safe precisely because we just
            // confirmed nothing exists, and it stops one failure blocking the shopper for an hour.
            _logger.LogInformation(
                "The duplicate create for {Reference} left no subscription behind; retrying with a fresh token.",
                subscriptionReference);

            request.UniquenessToken = Guid.NewGuid().ToString("N");

            return await CreateSubscriptionAsync(request, plan, customer, customerAlreadyExisted, site,
                customerReference, cancellationToken);
        }
    }

    private async Task<SubscriptionEnrollment> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request,
        SubscriptionPlan plan, BillingCustomer customer, bool customerAlreadyExisted, MaxioSite? site,
        string customerReference, CancellationToken cancellationToken)
    {
        var created = await _client.PostAsync<MaxioSubscriptionEnvelope>("subscriptions.json", request,
            request.UniquenessToken!, cancellationToken);

        var subscription = created.Subscription
                           ?? throw new BillingApiException(
                               "Maxio created a subscription but returned no subscription body.",
                               (int)HttpStatusCode.BadGateway);

        _logger.LogInformation(
            "Subscribed {CustomerReference} to {PlanHandle} as Maxio subscription {SubscriptionId} ({Reference}), state {State}.",
            customerReference, plan.Handle, subscription.Id, request.Subscription.Reference, subscription.State);

        return new SubscriptionEnrollment
        {
            Subscription = MapSubscription(subscription, plan, site),
            Customer = customer,
            AlreadyEnrolled = false,
            CustomerAlreadyExisted = customerAlreadyExisted
        };
    }

    /// <summary>
    /// Picks how Maxio should collect payment for a new subscription.
    ///
    /// eShopOnWeb captures no card details, so a plan that needs no payment method is billed by
    /// invoice - "remittance" on Relationship Invoicing sites, "invoice" on legacy ones. Left on
    /// the usual site default of "automatic", Maxio would try to charge at signup and reject the
    /// whole request for want of a payment profile.
    /// </summary>
    private string ResolvePaymentCollectionMethod(SubscriptionPlan plan, MaxioSite? site)
    {
        if (!string.IsNullOrWhiteSpace(_options.PaymentCollectionMethod))
        {
            return _options.PaymentCollectionMethod.Trim();
        }

        if (plan.RequiresPaymentMethod)
        {
            // The plan insists on a stored payment method, so only automatic collection makes
            // sense - and Maxio says so plainly when no payment profile exists for the customer.
            return "automatic";
        }

        return site?.RelationshipInvoicingEnabled == false ? "invoice" : "remittance";
    }

    /// <summary>
    /// Reads the billing site settings (currency and invoicing model). Cached, and never fatal:
    /// the integration falls back to safe defaults when the call fails.
    /// </summary>
    private async Task<MaxioSite?> ReadSiteAsync(CancellationToken cancellationToken)
    {
        if (_options.SiteCacheSeconds > 0 && _cache.TryGetValue<MaxioSite>(SiteCacheKey, out var cached))
        {
            return cached;
        }

        MaxioSite? site;

        try
        {
            var envelope = await _client.GetOrDefaultAsync<MaxioSiteEnvelope>("site.json", cancellationToken);
            site = envelope?.Site;
        }
        catch (BillingApiException ex)
        {
            _logger.LogWarning(ex, "Could not read the Maxio site settings; falling back to defaults.");
            site = null;
        }

        if (_options.SiteCacheSeconds > 0)
        {
            _cache.Set(SiteCacheKey, site, TimeSpan.FromSeconds(_options.SiteCacheSeconds));
        }

        return site;
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        _options.EnsureValid();

        var customerReference = BuildCustomerReference(subscriber);
        var customer = await FindCustomerAsync(customerReference, cancellationToken);

        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await ReadCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var plans = await SafeListPlansAsync(cancellationToken);
        var site = await ReadSiteAsync(cancellationToken);

        return subscriptions
            .Select(subscription => MapSubscription(subscription, FindPlanFor(subscription, plans), site))
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .ToList();
    }

    private async Task<(BillingCustomer Customer, bool AlreadyExisted)> EnsureCustomerAsync(SubscriberIdentity subscriber,
        string customerReference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(customerReference, cancellationToken);

        if (existing is not null)
        {
            return (existing, true);
        }

        var (firstName, lastName) = subscriber.ResolveName();

        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = customerReference
            },
            UniquenessToken = BuildUniquenessToken("customer", customerReference)
        };

        try
        {
            var created = await _client.PostAsync<MaxioCustomerEnvelope>("customers.json", request,
                request.UniquenessToken!, cancellationToken);

            var customer = created.Customer
                           ?? throw new BillingApiException("Maxio created a customer but returned no customer body.",
                               (int)HttpStatusCode.BadGateway);

            _logger.LogInformation("Created Maxio customer {CustomerId} for {CustomerReference}.",
                customer.Id, customerReference);

            return (MapCustomer(customer), false);
        }
        catch (BillingApiException ex) when (ex.StatusCode is (int)HttpStatusCode.Conflict
                                                 or (int)HttpStatusCode.UnprocessableEntity)
        {
            // Either the duplicate-prevention token was replayed (409) or the reference is
            // already taken (422). Both mean the customer exists - re-read rather than fail.
            var reread = await FindCustomerAsync(customerReference, cancellationToken);

            if (reread is null)
            {
                throw;
            }

            _logger.LogInformation(
                "Maxio already had customer {CustomerId} for {CustomerReference}; reusing it.",
                reread.Id, customerReference);

            return (reread, true);
        }
    }

    private async Task<BillingCustomer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}";
        var envelope = await _client.GetOrDefaultAsync<MaxioCustomerEnvelope>(url, cancellationToken);

        return envelope?.Customer is null ? null : MapCustomer(envelope.Customer);
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ReadCustomerSubscriptionsAsync(long customerId,
        CancellationToken cancellationToken)
    {
        var url = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var envelopes = await _client.GetOrDefaultAsync<List<MaxioSubscriptionEnvelope>>(url, cancellationToken);

        return envelopes?
                   .Select(envelope => envelope.Subscription)
                   .Where(subscription => subscription is not null)
                   .Select(subscription => subscription!)
                   .ToList()
               ?? (IReadOnlyList<MaxioSubscription>)Array.Empty<MaxioSubscription>();
    }

    /// <summary>
    /// Plans are only used to enrich the response, so a catalog hiccup must not fail a read of
    /// the shopper's own subscriptions.
    /// </summary>
    private async Task<IReadOnlyList<SubscriptionPlan>> SafeListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ListPlansAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is BillingApiException or BillingConfigurationException)
        {
            _logger.LogWarning(ex, "Could not read the Maxio plan catalog while listing subscriptions.");
            return Array.Empty<SubscriptionPlan>();
        }
    }

    private static SubscriptionPlan? FindPlanFor(MaxioSubscription subscription, IReadOnlyList<SubscriptionPlan> plans) =>
        subscription.Product?.Handle is { Length: > 0 } handle
            ? plans.FirstOrDefault(plan => string.Equals(plan.Handle, handle, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>
    /// True when the subscription represents an enrollment in the given plan. The product handle
    /// is authoritative; the reference is a fallback for sites where subscriptions carry no product.
    /// </summary>
    private static bool MatchesPlan(MaxioSubscription subscription, string planHandle, string customerReference)
    {
        if (subscription.Product?.Handle is { Length: > 0 } handle)
        {
            return string.Equals(handle, planHandle, StringComparison.OrdinalIgnoreCase);
        }

        return subscription.Reference is { Length: > 0 } reference &&
               reference.StartsWith(BaseSubscriptionReference(customerReference, planHandle), StringComparison.Ordinal);
    }

    /// <summary>
    /// Deterministic customer reference. Derived from the shopper's stable application identity so
    /// the same shopper always resolves to the same Maxio customer, with no local mapping table.
    /// </summary>
    private string BuildCustomerReference(SubscriberIdentity subscriber)
    {
        var userId = subscriber.UserId?.Trim();

        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentException("The subscriber has no stable user identifier.", nameof(subscriber));
        }

        return $"{_options.ReferencePrefix}-{userId.ToLowerInvariant()}";
    }

    private static string BaseSubscriptionReference(string customerReference, string planHandle) =>
        $"{customerReference}:{planHandle.ToLowerInvariant()}";

    /// <summary>
    /// Picks a subscription reference that is unique for this shopper and plan. Re-subscribing
    /// after a cancellation gets the next suffix rather than colliding with the old record.
    /// </summary>
    private static string BuildSubscriptionReference(string customerReference, string planHandle,
        IReadOnlyList<MaxioSubscription> existing)
    {
        var baseReference = BaseSubscriptionReference(customerReference, planHandle);
        var taken = existing
            .Select(subscription => subscription.Reference)
            .Where(reference => !string.IsNullOrEmpty(reference))
            .ToHashSet(StringComparer.Ordinal);

        if (!taken.Contains(baseReference))
        {
            return baseReference;
        }

        for (var attempt = 2; attempt < int.MaxValue; attempt++)
        {
            var candidate = $"{baseReference}:{attempt.ToString(CultureInfo.InvariantCulture)}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new BillingApiException($"Could not derive a unique subscription reference for {baseReference}.",
            (int)HttpStatusCode.InternalServerError);
    }

    /// <summary>
    /// A long, opaque, deterministic duplicate-prevention token. Deterministic is the point: two
    /// clicks of the same subscribe button produce the same token, and Maxio rejects the second
    /// with 409 instead of creating a second record.
    /// </summary>
    private static string BuildUniquenessToken(string scope, string reference)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"eshoponweb:{scope}:{reference}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, string? currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialPriceInCents = product.TrialPriceInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static BillingCustomer MapCustomer(MaxioCustomer customer) => new()
    {
        Id = customer.Id,
        Reference = customer.Reference,
        Email = customer.Email,
        FirstName = customer.FirstName,
        LastName = customer.LastName
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, SubscriptionPlan? plan,
        MaxioSite? site = null) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle ?? plan?.Handle,
        PlanName = subscription.Product?.Name ?? plan?.Name,
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : plan?.PriceInCents ?? 0,
        Currency = site?.Currency ?? plan?.Currency,
        Interval = subscription.Product?.Interval ?? plan?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit ?? plan?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference
    };
}
