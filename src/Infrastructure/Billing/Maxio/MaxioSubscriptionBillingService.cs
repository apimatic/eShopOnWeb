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
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Implements recurring-subscription billing on top of Maxio Advanced Billing.
///
/// Maxio is the system of record: eShopOnWeb stores nothing locally. The durable link between an
/// eShopOnWeb user and their Maxio customer is the customer <c>reference</c>, which is derived
/// from the user name. That keeps the integration correct even when the app runs against the
/// in-memory database, whose identity rows do not survive a restart.
/// </summary>
internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// States in which a subscription no longer entitles the shopper to the plan, and in which
    /// subscribing again should create a new subscription rather than return the old one.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended"
    };

    internal const string CustomerReferencePrefix = "eshoponweb-";

    private readonly MaxioApiClient _client;
    private readonly MaxioSiteContext _site;
    private readonly KeyedAsyncLock _subscriberLock;
    private readonly IOptionsMonitor<MaxioOptions> _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioApiClient client,
        MaxioSiteContext site,
        KeyedAsyncLock subscriberLock,
        IOptionsMonitor<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _site = site;
        _subscriberLock = subscriberLock;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = RequireProductFamilyHandle();
        var products = await _client.ListProductsForFamilyAsync(familyHandle, cancellationToken);
        var currency = await _site.GetCurrencyAsync(cancellationToken);

        return products
            .Where(p => p.ArchivedAt is null && !string.IsNullOrWhiteSpace(p.Handle))
            .OrderBy(p => p.PriceInCents)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => MapPlan(p, currency))
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Only products in the configured family may be subscribed to. This both gives the caller a
        // clean 404 for a typo and stops an arbitrary product handle on the site from being used.
        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionBillingException(
                $"Subscription plan '{request.PlanHandle}' was not found.", statusCode: 404);
        }

        var reference = CustomerReferenceFor(request.Subscriber);
        var currency = await _site.GetCurrencyAsync(cancellationToken);

        using var _ = await _subscriberLock.AcquireAsync(reference, cancellationToken);

        var customer = await EnsureCustomerAsync(request.Subscriber, reference, cancellationToken);

        var existing = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Subscriber {Reference} is already on plan {PlanHandle} (subscription {SubscriptionId}, state {State}); returning the existing subscription.",
                reference, plan.Handle, existing.Id, existing.State);
            return new SubscribeResult(MapSubscription(existing, currency), alreadySubscribed: true);
        }

        var paymentCollectionMethod = await _site.GetPaymentCollectionMethodAsync(cancellationToken);
        var created = await CreateSubscriptionAsync(
            customer, plan.Handle, paymentCollectionMethod,
            UniquenessToken(reference, plan.Handle, request.IdempotencyKey),
            cancellationToken);

        if (created is null)
        {
            // Maxio flagged the submission as a duplicate. Either the twin request already created
            // the subscription - in which case we return it - or the token collided with an older
            // submission inside the 60 minute window and a fresh token is the right answer.
            var raced = await FindLiveSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Duplicate subscribe for {Reference} on plan {PlanHandle} resolved to existing subscription {SubscriptionId}.",
                    reference, plan.Handle, raced.Id);
                return new SubscribeResult(MapSubscription(raced, currency), alreadySubscribed: true);
            }

            created = await CreateSubscriptionAsync(
                customer, plan.Handle, paymentCollectionMethod,
                uniquenessToken: Guid.NewGuid().ToString("N"),
                cancellationToken)
                ?? throw new SubscriptionBillingException(
                    "Maxio repeatedly rejected the subscription as a duplicate submission. Please retry in a few minutes.",
                    statusCode: 409);
        }

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} on plan {PlanHandle} for subscriber {Reference} (state {State}).",
            created.Id, plan.Handle, reference, created.State);

        return new SubscribeResult(MapSubscription(created, currency), alreadySubscribed: false);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        var reference = CustomerReferenceFor(subscriber);
        var customer = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        var currency = await _site.GetCurrencyAsync(cancellationToken);
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);

        return subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => MapSubscription(s, currency))
            .ToList();
    }

    /// <summary>
    /// The stable key that ties an eShopOnWeb user to a Maxio customer. Deriving it from the user
    /// name (rather than the identity row's surrogate id) keeps the same shopper mapped to the same
    /// Maxio customer across restarts, including on the in-memory database.
    /// </summary>
    internal static string CustomerReferenceFor(SubscriberIdentity subscriber) =>
        CustomerReferencePrefix + subscriber.UserName.Trim().ToLowerInvariant();

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, string reference, CancellationToken cancellationToken)
    {
        var existing = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null) return existing;

        var (firstName, lastName) = DeriveName(subscriber);
        var request = new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomerAttributes
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = reference
            }
        };

        try
        {
            var created = await _client.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for subscriber {Reference}.", created.Id, reference);
            return created;
        }
        catch (SubscriptionBillingException ex) when (IsDuplicateReference(ex))
        {
            // Another writer won the race on this reference. Maxio enforces uniqueness, so the
            // customer that now exists is the one we wanted.
            var raced = await _client.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Reused Maxio customer {CustomerId} for subscriber {Reference} after a create race.", raced.Id, reference);
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);

        return subscriptions
            .Where(s => IsLive(s.State))
            .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>Returns null when Maxio rejected the write as a duplicate submission.</summary>
    private async Task<MaxioSubscription?> CreateSubscriptionAsync(
        MaxioCustomer customer, string planHandle, string paymentCollectionMethod, string uniquenessToken, CancellationToken cancellationToken)
    {
        var request = new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = paymentCollectionMethod
            },
            UniquenessToken = uniquenessToken
        };

        try
        {
            return await _client.CreateSubscriptionAsync(request, cancellationToken);
        }
        catch (MaxioDuplicateSubmissionException)
        {
            return null;
        }
    }

    private string RequireProductFamilyHandle()
    {
        var handle = _options.CurrentValue.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new SubscriptionBillingException(
                "Maxio:ProductFamilyHandle is not configured, so no subscription plans can be offered.",
                statusCode: 502);
        }

        return handle!.Trim();
    }

    private static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state!);

    private static bool IsDuplicateReference(SubscriptionBillingException exception) =>
        exception.Errors.Any(e =>
            e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
            e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Deterministic duplicate-prevention token. Repeating the same subscribe for the same
    /// subscriber and plan reuses the token, so Maxio rejects the twin with 409 instead of
    /// creating a second subscription.
    /// </summary>
    internal static string UniquenessToken(string reference, string planHandle, string? idempotencyKey)
    {
        var material = string.Join('|', reference, planHandle.ToLowerInvariant(), idempotencyKey ?? "subscriber-plan");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "eshoponweb-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Maxio requires a first and last name on a customer, but eShopOnWeb identities only carry a
    /// user name and email. Use whatever the caller supplied and fall back to the email local part.
    /// </summary>
    internal static (string FirstName, string LastName) DeriveName(SubscriberIdentity subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) || !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (Fallback(subscriber.FirstName, "eShopOnWeb"), Fallback(subscriber.LastName, "Shopper"));
        }

        var localPart = subscriber.Email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => ("eShopOnWeb", "Shopper"),
            1 => (Titleize(parts[0]), "Shopper"),
            _ => (Titleize(parts[0]), Titleize(parts[^1]))
        };

        static string Fallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
    }

    private static string Titleize(string value) =>
        value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];

    private static SubscriptionPlan MapPlan(MaxioProduct product, string currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        RequiresPaymentMethod = product.RequireCreditCard,
        Taxable = product.Taxable,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        SetupFeeInCents = product.InitialChargeInCents,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    private static CustomerSubscription MapSubscription(MaxioSubscription subscription, string siteCurrency)
    {
        var live = IsLive(subscription.State);

        return new CustomerSubscription
        {
            Id = subscription.Id,
            State = subscription.State ?? "unknown",
            IsLive = live,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? siteCurrency : subscription.Currency!,
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            NextBillingAt = live ? subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt : null,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt,
            BalanceInCents = subscription.BalanceInCents,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod ?? string.Empty,
            CustomerId = subscription.Customer?.Id ?? 0,
            CustomerReference = subscription.Customer?.Reference
        };
    }
}
