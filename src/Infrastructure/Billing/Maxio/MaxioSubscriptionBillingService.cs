using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Billing.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Implements the subscription capability on top of Maxio Advanced Billing.
/// <para>
/// All of the flow logic lives here &#8212; ensuring the customer record, deduplicating enrollments
/// and projecting Maxio shapes onto the application models &#8212; so that
/// <see cref="MaxioApiClient"/> stays a literal transcription of the specification.
/// </para>
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// The path parameter of <c>listProductsForProductFamily</c> accepts "either the id of the
    /// product family or its handle prefixed with <c>handle:</c>". We always configure a handle,
    /// because Maxio reassigns numeric ids when a site is re-seeded.
    /// </summary>
    private const string HandlePrefix = "handle:";

    private readonly IMaxioApiClient _client;
    private readonly IMemoryCache _cache;
    private readonly StripedAsyncLock _subscribeLock;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioApiClient client,
        IMemoryCache cache,
        StripedAsyncLock subscribeLock,
        IOptionsMonitor<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _cache = cache;
        _subscribeLock = subscribeLock;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var familyHandle = RequireProductFamilyHandle(options);
        var cacheKey = $"maxio:plans:{familyHandle}";

        if (options.PlanCacheSeconds > 0 &&
            _cache.TryGetValue<IReadOnlyList<SubscriptionPlan>>(cacheKey, out var cached) &&
            cached is not null)
        {
            return cached;
        }

        var products = await _client.ListProductsForProductFamilyAsync(
            HandlePrefix + familyHandle,
            includeArchived: false,
            cancellationToken);

        var plans = products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ThenBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Loaded {PlanCount} subscription plan(s) from Maxio product family {ProductFamily}.",
            plans.Count,
            familyHandle);

        if (options.PlanCacheSeconds > 0)
        {
            _cache.Set<IReadOnlyList<SubscriptionPlan>>(
                cacheKey,
                plans,
                TimeSpan.FromSeconds(options.PlanCacheSeconds));
        }

        return plans;
    }

    public async Task<SubscribeResult> SubscribeAsync(
        SubscribeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var options = _options.CurrentValue;
        var planHandle = ResolvePlanHandle(command.PlanHandle, options);

        // Validate against the configured catalog before touching customer state, so an unknown
        // plan can never leave a half-finished customer record behind.
        var plan = (await GetPlansAsync(cancellationToken))
            .FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new SubscriptionPlanNotFoundException(planHandle);

        var customerReference = BuildCustomerReference(command.Subscriber, options);
        var lockTimeout = TimeSpan.FromSeconds(options.SubscribeLockTimeoutSeconds);

        using var _ = await AcquireSubscribeLockAsync(customerReference, lockTimeout, cancellationToken);

        var customer = await EnsureCustomerAsync(command.Subscriber, customerReference, cancellationToken);

        // 1. An explicit idempotency key is an exact replay marker: same key, same subscriber,
        //    same operation. Whatever it produced the first time is the answer every time.
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var keyedReference = BuildSubscriptionReference(customerReference, command.IdempotencyKey!);
            var replayed = await _client.FindSubscriptionByReferenceAsync(keyedReference, cancellationToken);

            if (replayed is not null && replayed.Customer?.Id == customer.Id)
            {
                _logger.LogInformation(
                    "Replayed idempotency key for customer {CustomerId}; returning existing subscription {SubscriptionId}.",
                    customer.Id,
                    replayed.Id);

                return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, ToSubscription(replayed));
            }

            return await CreateSubscriptionAsync(customer, plan, command.PricePointHandle, keyedReference, cancellationToken);
        }

        // 2. Without a key, the shopper's own subscription list is the deduplication source of
        //    truth: an existing enrollment on the same plan means this is a repeat submit.
        var existing = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        var live = existing.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
            SubscriptionStates.IsLive(subscription.State));

        if (live is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerId} is already subscribed to {PlanHandle} (subscription {SubscriptionId}, state {State}); not creating a duplicate.",
                customer.Id,
                planHandle,
                live.Id,
                live.State);

            return new SubscribeResult(SubscribeOutcome.AlreadySubscribed, ToSubscription(live));
        }

        // Ended enrollments on the same plan stay in the history, so suffix the reference to keep
        // it unique and to record which attempt this is.
        var priorAttempts = existing.Count(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

        var reference = BuildSubscriptionReference(
            customerReference,
            priorAttempts == 0
                ? planHandle
                : string.Create(CultureInfo.InvariantCulture, $"{planHandle}-{priorAttempts + 1}"));

        return await CreateSubscriptionAsync(customer, plan, command.PricePointHandle, reference, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriberSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var customerReference = BuildCustomerReference(subscriber, _options.CurrentValue);
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);

        if (customer is null)
        {
            _logger.LogDebug(
                "No Maxio customer exists for reference {CustomerReference}; the subscriber has no subscriptions.",
                customerReference);

            return Array.Empty<SubscriberSubscription>();
        }

        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .Select(ToSubscription)
            .OrderByDescending(subscription => subscription.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(subscription => subscription.Id)
            .ToList();
    }

    private async Task<SubscribeResult> CreateSubscriptionAsync(
        MaxioCustomer customer,
        SubscriptionPlan plan,
        string? pricePointHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        if (plan.RequiresPaymentMethod)
        {
            // Maxio would reject the signup anyway; failing here gives the caller a message that
            // says what to fix instead of a raw 422 from the provider.
            throw new BillingProviderException(
                $"Plan '{plan.Handle}' requires a stored payment method, which this integration does not capture. " +
                "Configure the plan with 'payment method not required', or extend the integration with Chargify.js payment capture.",
                statusCode: (int)HttpStatusCode.UnprocessableEntity);
        }

        var collectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken);

        var created = await _client.CreateSubscriptionAsync(
            new MaxioCreateSubscription
            {
                ProductHandle = plan.Handle,
                ProductPricePointHandle = string.IsNullOrWhiteSpace(pricePointHandle) ? null : pricePointHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = collectionMethod,
                Reference = reference
            },
            cancellationToken);

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} (state {State}).",
            created.Id,
            customer.Id,
            plan.Handle,
            created.State);

        return new SubscribeResult(SubscribeOutcome.Created, ToSubscription(created));
    }

    /// <summary>
    /// Decides which Collection-Method to request on signup.
    /// <para>
    /// This integration stores no payment method, so asking for <c>automatic</c> collection makes
    /// Maxio try to charge a card that does not exist and the signup fails. Invoice-style
    /// collection is therefore the correct default; which value means that depends on the
    /// architecture of the site, so we read it from <c>GET /site.json</c> rather than assuming.
    /// An explicit <c>Maxio:PaymentCollectionMethod</c> always wins.
    /// </para>
    /// </summary>
    private async Task<string> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        if (!string.IsNullOrWhiteSpace(options.PaymentCollectionMethod))
        {
            var configured = options.PaymentCollectionMethod.Trim().ToLowerInvariant();

            if (!MaxioOptions.CollectionMethods.Contains(configured))
            {
                throw new BillingConfigurationException(
                    $"Maxio:PaymentCollectionMethod must be one of {string.Join(", ", MaxioOptions.CollectionMethods)}. " +
                    $"Value provided: '{options.PaymentCollectionMethod}'.");
            }

            return configured;
        }

        const string cacheKey = "maxio:site";

        if (options.SiteCacheSeconds > 0 &&
            _cache.TryGetValue<string>(cacheKey, out var cached) &&
            !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        var site = await _client.ReadSiteAsync(cancellationToken);
        var method = site.RelationshipInvoicingEnabled ? "remittance" : "invoice";

        _logger.LogInformation(
            "Maxio site {Subdomain} has relationship invoicing {RelationshipInvoicing}; subscribing with payment collection method {CollectionMethod}.",
            site.Subdomain,
            site.RelationshipInvoicingEnabled,
            method);

        if (options.SiteCacheSeconds > 0)
        {
            _cache.Set(cacheKey, method, TimeSpan.FromSeconds(options.SiteCacheSeconds));
        }

        return method;
    }

    /// <summary>
    /// Returns the Maxio customer for this subscriber, creating it on first use. Safe to call
    /// concurrently: a losing race shows up as a 422 on the uniqueness of the reference, which is
    /// resolved by re-reading the record the winner created.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(
        SubscriberIdentity subscriber,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = ResolveName(subscriber);

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

            _logger.LogInformation(
                "Created Maxio customer {CustomerId} for reference {CustomerReference}.",
                created.Id,
                customerReference);

            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            // Another writer may have created the same reference between our lookup and our POST.
            var raced = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Maxio customer {CustomerId} for reference {CustomerReference} was created concurrently; reusing it.",
                    raced.Id,
                    customerReference);

                return raced;
            }

            throw;
        }
    }

    private async Task<IDisposable> AcquireSubscribeLockAsync(
        string customerReference,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _subscribeLock.AcquireAsync(customerReference, timeout, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new BillingProviderException(
                "Another subscribe request for this shopper is still in progress. Please retry in a moment.",
                statusCode: (int)HttpStatusCode.Conflict,
                errors: null,
                innerException: ex);
        }
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

        throw new BillingRequestException(
            "No plan was specified and no Maxio:DefaultPlanHandle is configured. Supply 'planHandle' in the request.");
    }

    private static string RequireProductFamilyHandle(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            throw new BillingConfigurationException(
                "Maxio:ProductFamilyHandle is not configured; there is no plan catalog to read.");
        }

        return options.ProductFamilyHandle.Trim();
    }

    /// <summary>
    /// Builds the value written to the <c>reference</c> field of the Maxio customer. This is the
    /// join key between an eShopOnWeb user and their billing record, so it must be stable for the
    /// lifetime of the account; see <see cref="MaxioCustomerReferenceSource"/> for the trade-off
    /// between the user id and the email.
    /// </summary>
    private static string BuildCustomerReference(SubscriberIdentity subscriber, MaxioOptions options)
    {
        var key = options.CustomerReferenceSource switch
        {
            MaxioCustomerReferenceSource.UserId => subscriber.UserId,
            _ => subscriber.Email
        };

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new BillingConfigurationException(
                $"Cannot build a Maxio customer reference: the subscriber has no {options.CustomerReferenceSource} value.");
        }

        return $"{options.ReferencePrefix}:{key.Trim().ToLowerInvariant()}";
    }

    private static string BuildSubscriptionReference(string customerReference, string discriminator) =>
        $"{customerReference}:{discriminator.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Maxio requires a non-blank first and last name. eShopOnWeb identities carry only an email,
    /// so unless the caller supplies names we derive something readable from the local part rather
    /// than sending placeholders.
    /// </summary>
    internal static (string FirstName, string LastName) ResolveName(SubscriberIdentity subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
        {
            return (first, last);
        }

        var localPart = subscriber.Email?.Split('@', 2)[0] ?? string.Empty;
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var derivedFirst = parts.Length > 0 ? Capitalize(parts[0]) : "eShopOnWeb";
        var derivedLast = parts.Length > 1 ? Capitalize(string.Join(' ', parts[1..])) : "Shopper";

        return (
            string.IsNullOrEmpty(first) ? derivedFirst : first,
            string.IsNullOrEmpty(last) ? derivedLast : last);
    }

    private static string Capitalize(string value) =>
        value.Length switch
        {
            0 => value,
            1 => value.ToUpperInvariant(),
            _ => char.ToUpperInvariant(value[0]) + value[1..]
        };

    private static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        ProductFamilyHandle = product.ProductFamily?.Handle,
        PricePointHandle = product.ProductPricePointHandle,
        RequiresPaymentMethod = product.RequireCreditCard,
        TrialInterval = product.TrialInterval is > 0 ? product.TrialInterval : null,
        TrialIntervalUnit = product.TrialInterval is > 0 ? product.TrialIntervalUnit : null,
        SetupFeeInCents = product.InitialChargeInCents is > 0 ? product.InitialChargeInCents : null,
        Taxable = product.Taxable,
        ArchivedAt = product.ArchivedAt
    };

    private static SubscriberSubscription ToSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        Currency = subscription.Currency,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        TrialEndedAt = subscription.TrialEndedAt,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference
    };
}
