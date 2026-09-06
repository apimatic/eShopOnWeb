using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
/// Implements the subscription capability on top of Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// Maxio is the system of record: plans, customers and subscriptions are read back from it rather
/// than mirrored locally, so the answers survive an eShopOnWeb restart and stay correct when a
/// subscription is changed from the Maxio UI.
/// </remarks>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>Maxio caps <c>per_page</c> at 200.</summary>
    private const int PageSize = 200;

    /// <summary>Guards against an unbounded loop if the provider ever stops advancing pages.</summary>
    private const int MaxPages = 25;

    private const string HandlePathPrefix = "handle:";

    private readonly IMaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly SubscriberGate _gate;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IMemoryCache cache,
        SubscriberGate gate,
        IOptionsMonitor<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _cache = cache;
        _gate = gate;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var cacheKey = $"maxio:plans:{options.ProductFamilyHandle}";

        if (options.PlanCacheDuration > TimeSpan.Zero &&
            _cache.TryGetValue(cacheKey, out IReadOnlyList<SubscriptionPlan>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var products = await ListAllProductsAsync(options.ProductFamilyHandle, cancellationToken);

        var plans = products
            // An archived product is still returned by the catalogue; it must not be offered.
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(MapPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.PlanCacheDuration > TimeSpan.Zero)
        {
            _cache.Set(cacheKey, (IReadOnlyList<SubscriptionPlan>)plans, options.PlanCacheDuration);
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle ?? string.Empty);
        }

        planHandle = planHandle.Trim();

        // Resolving through the plan catalogue also confines enrolment to the configured family.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customerReference = BuildCustomerReference(subscriber);
        var subscriptionReference = string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : BuildSubscriptionReference(customerReference, plan.Handle, idempotencyKey!);

        using var _ = await _gate.AcquireAsync(customerReference, cancellationToken);

        try
        {
            // 1. A caller-supplied idempotency key makes the enrolment replayable: the same key
            //    always resolves to the subscription the first call created.
            if (subscriptionReference is not null)
            {
                var replay = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (replay is not null)
                {
                    _logger.LogInformation(
                        "Replayed enrolment for subscriber {SubscriberReference} on plan {PlanHandle}; subscription {SubscriptionId} already exists.",
                        customerReference, plan.Handle, replay.Id);

                    return new SubscribeResult(MapSubscription(replay), AlreadySubscribed: true);
                }
            }

            var customer = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken);

            // 2. Without a key, an existing live subscription to the same plan is the answer -
            //    this is what makes a double-clicked "Subscribe" harmless.
            var existing = (await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken))
                .FirstOrDefault(subscription =>
                    SubscriptionStates.IsLive(subscription.State) &&
                    string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Subscriber {SubscriberReference} is already on plan {PlanHandle} via subscription {SubscriptionId}; not enrolling again.",
                    customerReference, plan.Handle, existing.Id);

                return new SubscribeResult(MapSubscription(existing), AlreadySubscribed: true);
            }

            var created = await CreateSubscriptionAsync(plan.Handle, customer.Id, subscriptionReference, cancellationToken);

            _logger.LogInformation(
                "Enrolled subscriber {SubscriberReference} on plan {PlanHandle}; subscription {SubscriptionId} is {State}.",
                customerReference, plan.Handle, created.Id, created.State);

            return new SubscribeResult(MapSubscription(created), AlreadySubscribed: false);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customerReference = BuildCustomerReference(subscriber);

        try
        {
            var customer = await FindCustomerAsync(customerReference, cancellationToken);
            if (customer is null)
            {
                // The shopper has simply never subscribed; that is not an error.
                return Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

            return subscriptions
                .Select(MapSubscription)
                .OrderByDescending(subscription => subscription.IsLive)
                .ThenByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
    }

    private async Task<IReadOnlyList<MaxioProduct>> ListAllProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var familyPathValue = HandlePathPrefix + productFamilyHandle;
        var products = new List<MaxioProduct>();

        try
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var batch = await _client.ListProductsForProductFamilyAsync(familyPathValue, page, PageSize, cancellationToken);
                products.AddRange(batch);

                if (batch.Count < PageSize)
                {
                    return products;
                }
            }

            _logger.LogWarning(
                "Stopped listing plans for product family {ProductFamilyHandle} after {MaxPages} pages.",
                productFamilyHandle, MaxPages);

            return products;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new BillingProviderException(
                $"Maxio product family '{productFamilyHandle}' does not exist on this site. Check the '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ProductFamilyHandle)}' setting.",
                (int)ex.StatusCode.Value,
                ex.Errors,
                ex);
        }
        catch (MaxioApiException ex)
        {
            throw Translate(ex);
        }
    }

    /// <summary>
    /// Returns the Maxio customer for this shopper, creating it on first use. Safe to run
    /// concurrently: a lost create race is resolved by re-reading the reference, which Maxio
    /// enforces as unique.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, string reference, CancellationToken cancellationToken)
    {
        // Enrolment is rare and a stale customer id would fail the create, so it is worth one
        // round trip to resolve the customer from Maxio rather than trusting the cache here.
        var existing = await FindCustomerAsync(reference, cancellationToken, bypassCache: true);
        if (existing is not null)
        {
            return existing;
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
                    Reference = reference
                },
                cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} for subscriber {SubscriberReference}.", created.Id, reference);

            CacheCustomer(reference, created);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsValidationFailure)
        {
            // Most likely another request created the customer first; the reference is unique,
            // so a successful re-read proves that is what happened.
            var raced = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            CacheCustomer(reference, raced);
            return raced;
        }
    }

    private async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken, bool bypassCache = false)
    {
        var cacheKey = CustomerCacheKey(reference);

        if (!bypassCache && _cache.TryGetValue(cacheKey, out MaxioCustomer? cached) && cached is not null)
        {
            return cached;
        }

        var customer = await _client.ReadCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is not null)
        {
            CacheCustomer(reference, customer);
        }

        return customer;
    }

    private void CacheCustomer(string reference, MaxioCustomer customer)
    {
        var duration = _options.CurrentValue.CustomerCacheDuration;
        if (duration > TimeSpan.Zero)
        {
            _cache.Set(CustomerCacheKey(reference), customer, duration);
        }
    }

    private static string CustomerCacheKey(string reference) => $"maxio:customer:{reference}";

    private async Task<MaxioSubscription> CreateSubscriptionAsync(
        string planHandle,
        int customerId,
        string? subscriptionReference,
        CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscription
        {
            // The handle is the durable identifier; Maxio reassigns numeric product ids on re-seed.
            ProductHandle = planHandle,
            CustomerId = customerId,
            Reference = subscriptionReference,
            PaymentCollectionMethod = _options.CurrentValue.PaymentCollectionMethod
        };

        try
        {
            return await _client.CreateSubscriptionAsync(request, cancellationToken);
        }
        catch (MaxioApiException ex) when (subscriptionReference is not null && ex.IsValidationFailure)
        {
            // A subscription reference is unique per site, so a rejected create whose reference now
            // resolves means a concurrent identical request won; return its subscription.
            var raced = await _client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return raced;
        }
    }

    /// <summary>
    /// The Maxio customer <c>reference</c> for an eShopOnWeb user. Deterministic, so re-running
    /// eShopOnWeb against the same Maxio site reuses the same customer instead of creating a new
    /// one, and prefixed so this application's customers are identifiable on a shared site.
    /// </summary>
    private string BuildCustomerReference(SubscriberIdentity subscriber) =>
        $"{_options.CurrentValue.CustomerReferencePrefix}:{subscriber.UserName.ToLowerInvariant()}";

    /// <summary>
    /// The Maxio subscription <c>reference</c> for a caller-supplied idempotency key. Maxio enforces
    /// uniqueness on this value, which is what makes a repeated enrolment request safe at the
    /// provider rather than only in this process.
    /// </summary>
    private static string BuildSubscriptionReference(string customerReference, string planHandle, string idempotencyKey)
    {
        var material = $"{customerReference}|{planHandle}|{idempotencyKey}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));

        return "sub-" + Convert.ToHexString(digest).Substring(0, 32).ToLowerInvariant();
    }

    /// <summary>
    /// Maxio requires a first and last name on a customer, but eShopOnWeb accounts carry no name.
    /// These are derived from the account's e-mail so they are stable and recognisable in Maxio.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(SubscriberIdentity subscriber)
    {
        var email = subscriber.Email;
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email.Substring(0, atIndex) : email;
        var domain = atIndex >= 0 && atIndex < email.Length - 1 ? email.Substring(atIndex + 1) : string.Empty;

        var tokens = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 0)
            .ToArray();

        if (tokens.Length >= 2)
        {
            return (Titleize(tokens[0]), Titleize(string.Join(" ", tokens.Skip(1))));
        }

        var first = tokens.Length == 1 ? Titleize(tokens[0]) : "eShopOnWeb";
        var domainLabel = domain.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var last = string.IsNullOrWhiteSpace(domainLabel) ? first : Titleize(domainLabel);

        return (first, last);
    }

    private static string Titleize(string value) =>
        value.Length == 0
            ? value
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value.Substring(1);

    private static SubscriptionPlan MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequiresPaymentMethod = product.RequireCreditCard,
        SetupFeeInCents = product.InitialChargeInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        PricePointName = product.ProductPricePointName,
        ProviderProductId = product.Id
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        // Maxio's next_assessment_at is when it will next attempt to capture payment.
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Reference = subscription.Reference,
        Customer = subscription.Customer is null
            ? null
            : new BillingCustomer
            {
                Id = subscription.Customer.Id,
                Reference = subscription.Customer.Reference,
                Email = subscription.Customer.Email,
                FirstName = subscription.Customer.FirstName,
                LastName = subscription.Customer.LastName
            }
    };

    /// <summary>
    /// Turns a provider-level failure into the application-level contract: a rejected request is a
    /// caller error, anything else is a gateway failure.
    /// </summary>
    private static BillingProviderException Translate(MaxioApiException exception)
    {
        var statusCode = exception.StatusCode.HasValue ? (int)exception.StatusCode.Value : (int?)null;

        if (!exception.IsValidationFailure)
        {
            return new BillingProviderException(exception.Message, statusCode, exception.Errors, exception);
        }

        // A rejection is the caller's to act on, so lead with what the provider actually objected
        // to rather than with which internal call carried the request.
        var message = exception.Errors.Count > 0
            ? string.Join(" ", exception.Errors)
            : exception.Message;

        return new BillingValidationException(message, statusCode, exception.Errors);
    }
}
