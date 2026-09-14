using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing. Maxio is the system of record:
/// nothing here is mirrored into the eShopOnWeb database, so the answers survive a restart of
/// this application and stay correct if a subscription is changed in the Maxio UI.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    /// <summary>
    /// Prefix on every reference this application writes into Maxio, so records it owns are
    /// obvious in the Maxio UI and cannot collide with references from another integration.
    /// </summary>
    internal const string ReferencePrefix = "eshoponweb";

    private const string PlanCacheKey = "maxio:plans";
    private const string SiteCacheKey = "maxio:site";
    private static readonly TimeSpan PlanCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SiteCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SubscribeLockTimeout = TimeSpan.FromSeconds(30);

    private readonly IMaxioApiClient _client;
    private readonly MaxioSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly MaxioSubscriberLocks _locks;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient client,
        IOptions<MaxioSettings> settings,
        IMemoryCache cache,
        MaxioSubscriberLocks locks,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _cache = cache;
        _locks = locks;
        _logger = logger;
    }

    public async Task<SubscriptionResult<IReadOnlyList<SubscriptionPlan>>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        if (NotConfigured<IReadOnlyList<SubscriptionPlan>>() is { } notConfigured)
        {
            return notConfigured;
        }

        try
        {
            var plans = await GetPlansAsync(cancellationToken);
            return SubscriptionResult<IReadOnlyList<SubscriptionPlan>>.Success(plans);
        }
        catch (MaxioApiException ex)
        {
            return Failure<IReadOnlyList<SubscriptionPlan>>(ex, "The subscription plans could not be loaded from Maxio.");
        }
    }

    public async Task<SubscriptionResult<IReadOnlyList<CustomerSubscription>>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        if (NotConfigured<IReadOnlyList<CustomerSubscription>>() is { } notConfigured)
        {
            return notConfigured;
        }

        try
        {
            var customer = await _client.FindCustomerByReferenceAsync(BuildCustomerReference(subscriber), cancellationToken);
            if (customer is null)
            {
                // Never subscribed, so no billing customer exists yet. Not an error.
                return SubscriptionResult<IReadOnlyList<CustomerSubscription>>.Success(Array.Empty<CustomerSubscription>());
            }

            var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var mapped = subscriptions
                .Select(MapSubscription)
                .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
                .ToList();

            return SubscriptionResult<IReadOnlyList<CustomerSubscription>>.Success(mapped);
        }
        catch (MaxioApiException ex)
        {
            return Failure<IReadOnlyList<CustomerSubscription>>(ex, "The subscriptions could not be loaded from Maxio.");
        }
    }

    public async Task<SubscriptionResult<SubscriptionEnrollment>> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (NotConfigured<SubscriptionEnrollment>() is { } notConfigured)
        {
            return notConfigured;
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return SubscriptionResult<SubscriptionEnrollment>.Failed(
                SubscriptionFailure.InvalidRequest,
                "A plan handle is required.");
        }

        var customerReference = BuildCustomerReference(request.Subscriber);
        var subscribeLock = _locks.For(customerReference);

        if (!await subscribeLock.WaitAsync(SubscribeLockTimeout, cancellationToken))
        {
            return SubscriptionResult<SubscriptionEnrollment>.Failed(
                SubscriptionFailure.Conflict,
                "Another subscription request for this account is still in progress. Please try again in a moment.");
        }

        try
        {
            return await SubscribeCoreAsync(request, customerReference, cancellationToken);
        }
        catch (MaxioApiException ex)
        {
            return Failure<SubscriptionEnrollment>(ex, "The subscription could not be created in Maxio.");
        }
        finally
        {
            subscribeLock.Release();
        }
    }

    private async Task<SubscriptionResult<SubscriptionEnrollment>> SubscribeCoreAsync(
        SubscribeRequest request,
        string customerReference,
        CancellationToken cancellationToken)
    {
        // Resolving the plan against the configured product family is also the authorisation
        // check: it stops a caller subscribing to an arbitrary product elsewhere on the site.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle.Trim(), StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            return SubscriptionResult<SubscriptionEnrollment>.Failed(
                SubscriptionFailure.PlanNotFound,
                string.Format(CultureInfo.InvariantCulture, "There is no subscription plan with handle '{0}'.", request.PlanHandle));
        }

        if (plan.RequiresPaymentMethod)
        {
            return SubscriptionResult<SubscriptionEnrollment>.Failed(
                SubscriptionFailure.UpstreamRejected,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Plan '{0}' requires a stored payment method. This integration does not capture payment details, so it cannot enroll subscribers in that plan.",
                    plan.Handle));
        }

        var customer = await EnsureCustomerAsync(request.Subscriber, customerReference, cancellationToken);

        // Authoritative duplicate check: ask Maxio what this customer already has.
        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}); returning the existing subscription.",
                customer.Id,
                plan.Handle,
                existing.Id);

            return SubscriptionResult<SubscriptionEnrollment>.Success(new SubscriptionEnrollment
            {
                Subscription = MapSubscription(existing),
                AlreadyExisted = true
            });
        }

        var paymentCollectionMethod = await ResolvePaymentCollectionMethodAsync(cancellationToken);
        var subscriptionReference = BuildSubscriptionReference(request.Subscriber, plan.Handle);

        var attributes = new MaxioSubscriptionAttributes
        {
            ProductHandle = plan.Handle,
            CustomerId = customer.Id,
            Reference = subscriptionReference,
            PaymentCollectionMethod = paymentCollectionMethod
        };

        try
        {
            var created = await _client.CreateSubscriptionAsync(attributes, request.IdempotencyKey, cancellationToken);
            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle} in state {State}.",
                created.Id,
                customer.Id,
                plan.Handle,
                created.State);

            return SubscriptionResult<SubscriptionEnrollment>.Success(new SubscriptionEnrollment
            {
                Subscription = MapSubscription(created),
                AlreadyExisted = false
            });
        }
        catch (MaxioApiException ex) when (ex.IsReferenceAlreadyTaken)
        {
            // Another attempt for this subscriber and plan got there first - possibly one of
            // ours that timed out on the way back. Whatever it created is the right answer.
            return await ResolveAfterCollisionAsync(customer, plan, attributes, request, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateSubmission)
        {
            // The caller's idempotency key was replayed. Maxio will not say what happened to
            // the original request, so look for its result.
            var duplicate = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (duplicate is not null)
            {
                return SubscriptionResult<SubscriptionEnrollment>.Success(new SubscriptionEnrollment
                {
                    Subscription = MapSubscription(duplicate),
                    AlreadyExisted = true
                });
            }

            return SubscriptionResult<SubscriptionEnrollment>.Failed(
                SubscriptionFailure.Conflict,
                "An earlier request with this idempotency key was already submitted to Maxio and did not produce a subscription. Retry with a new idempotency key.",
                ex.Errors);
        }
        catch (MaxioApiException ex) when (ex.IsTransportFailure)
        {
            // The request may or may not have been processed. If it was, the subscription is
            // there now and returning it beats reporting a failure the shopper cannot act on.
            var recovered = await TryRecoverAfterTransportFailureAsync(customer.Id, plan.Handle, cancellationToken);
            if (recovered is not null)
            {
                _logger.LogWarning(
                    "The create call for customer {CustomerId} on plan {PlanHandle} failed in transit, but subscription {SubscriptionId} exists; treating it as the result.",
                    customer.Id,
                    plan.Handle,
                    recovered.Id);

                return SubscriptionResult<SubscriptionEnrollment>.Success(new SubscriptionEnrollment
                {
                    Subscription = MapSubscription(recovered),
                    AlreadyExisted = true
                });
            }

            throw;
        }
    }

    /// <summary>
    /// Handles a subscription reference collision. If the collision is with a subscription that
    /// is still in force, that is the idempotent answer. If it is with an old, finished
    /// subscription, the subscriber is legitimately signing up again and needs a fresh
    /// reference, since Maxio keeps references unique for the life of the site.
    /// </summary>
    private async Task<SubscriptionResult<SubscriptionEnrollment>> ResolveAfterCollisionAsync(
        MaxioCustomer customer,
        SubscriptionPlan plan,
        MaxioSubscriptionAttributes attributes,
        SubscribeRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "A concurrent request already created subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                existing.Id,
                customer.Id,
                plan.Handle);

            return SubscriptionResult<SubscriptionEnrollment>.Success(new SubscriptionEnrollment
            {
                Subscription = MapSubscription(existing),
                AlreadyExisted = true
            });
        }

        attributes.Reference = attributes.Reference + ":" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

        var created = await _client.CreateSubscriptionAsync(attributes, request.IdempotencyKey, cancellationToken);
        _logger.LogInformation(
            "Re-subscribed customer {CustomerId} to plan {PlanHandle} as subscription {SubscriptionId}; the original reference belongs to an ended subscription.",
            customer.Id,
            plan.Handle,
            created.Id);

        return SubscriptionResult<SubscriptionEnrollment>.Success(new SubscriptionEnrollment
        {
            Subscription = MapSubscription(created),
            AlreadyExisted = false
        });
    }

    private async Task<MaxioSubscription?> TryRecoverAfterTransportFailureAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        try
        {
            return await FindLiveSubscriptionAsync(customerId, planHandle, cancellationToken);
        }
        catch (MaxioApiException)
        {
            // Maxio is still unreachable; let the original failure stand.
            return null;
        }
    }

    /// <summary>
    /// Finds or creates the Maxio customer for this subscriber. The eShopOnWeb user key is
    /// stored as the customer reference, which Maxio keeps unique per site - so two racing
    /// requests can never end up with two customers for one shopper.
    /// </summary>
    private async Task<MaxioCustomer> EnsureCustomerAsync(Subscriber subscriber, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(subscriber);

        var attributes = new MaxioCustomerAttributes
        {
            FirstName = firstName,
            LastName = lastName,
            Email = subscriber.Email,
            Organization = subscriber.Organization,
            Reference = customerReference
        };

        try
        {
            var created = await _client.CreateCustomerAsync(attributes, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, customerReference);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsReferenceAlreadyTaken)
        {
            // Someone else created it between the lookup and the create. Read it back.
            var raced = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions.FirstOrDefault(s =>
            SubscriptionStates.IsLive(s.State) &&
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Picks the collection method for a signup with no payment method on file. Sites collect
    /// automatically by default, which fails at signup when there is no card, so this asks for
    /// invoicing instead - under whichever name the site's invoicing architecture uses.
    /// </summary>
    private async Task<string> ResolvePaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var site = await GetSiteAsync(cancellationToken);
        return site?.RelationshipInvoicingEnabled == true ? "remittance" : "invoice";
    }

    private async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(PlanCacheKey, out IReadOnlyList<SubscriptionPlan>? cached) && cached is not null)
        {
            return cached;
        }

        var site = await GetSiteAsync(cancellationToken);
        var products = await _client.ListProductsForFamilyAsync(_settings.ProductFamilyHandle!, cancellationToken);

        var plans = products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .Select(p => MapPlan(p, site?.Currency))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cache.Set(PlanCacheKey, (IReadOnlyList<SubscriptionPlan>)plans, PlanCacheDuration);
        return plans;
    }

    private async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(SiteCacheKey, out MaxioSite? cached))
        {
            return cached;
        }

        var site = await _client.ReadSiteAsync(cancellationToken);
        _cache.Set(SiteCacheKey, site, SiteCacheDuration);
        return site;
    }

    private static SubscriptionPlan MapPlan(MaxioProduct product, string? currency) => new()
    {
        Handle = product.Handle!,
        Name = string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? "month",
        RequiresPaymentMethod = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        PricePointHandle = product.ProductPricePointHandle,
        TrialPriceInCents = product.TrialPriceInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,

        // product_price_in_cents is what this subscription is actually billed, which can differ
        // from the product's current list price if the product was repriced after signup.
        PriceInCents = subscription.ProductPriceInCents != 0
            ? subscription.ProductPriceInCents
            : subscription.Product?.PriceInCents ?? 0,
        Currency = subscription.Currency,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference
    };

    internal static string BuildCustomerReference(Subscriber subscriber) =>
        ReferencePrefix + ":" + subscriber.UserKey.Trim().ToLowerInvariant();

    internal static string BuildSubscriptionReference(Subscriber subscriber, string planHandle) =>
        BuildCustomerReference(subscriber) + ":" + planHandle.Trim().ToLowerInvariant();

    /// <summary>
    /// Splits the subscriber's name into the first/last pair Maxio requires. Falls back to the
    /// email local part, because eShopOnWeb identities carry no name of their own.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(Subscriber subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
        {
            return (first!, last!);
        }

        var localPart = subscriber.Email.Split('@')[0];
        var words = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        if (string.IsNullOrEmpty(first))
        {
            first = words.Length > 0 ? Capitalize(words[0]) : subscriber.Email;
        }

        if (string.IsNullOrEmpty(last))
        {
            last = words.Length > 1 ? Capitalize(string.Join(" ", words.Skip(1))) : "User";
        }

        return (first!, last!);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);

    private SubscriptionResult<T>? NotConfigured<T>()
    {
        var errors = _settings.GetConfigurationErrors();
        if (errors.Count == 0)
        {
            return null;
        }

        _logger.LogError("Maxio subscription billing is unavailable: {Errors}", string.Join(" ", errors));

        return SubscriptionResult<T>.Failed(
            SubscriptionFailure.NotConfigured,
            "Subscription billing is not configured for this deployment.",
            errors);
    }

    private SubscriptionResult<T> Failure<T>(MaxioApiException exception, string message)
    {
        if (exception.IsRejection)
        {
            return SubscriptionResult<T>.Failed(SubscriptionFailure.UpstreamRejected, exception.Message, exception.Errors);
        }

        _logger.LogError(exception, "{Message}", message);
        return SubscriptionResult<T>.Failed(SubscriptionFailure.UpstreamUnavailable, message, exception.Errors);
    }
}
