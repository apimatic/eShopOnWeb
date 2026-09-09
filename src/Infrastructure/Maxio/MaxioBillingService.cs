using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingSubscription;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Orchestrates the subscribe flow against Maxio Advanced Billing and enforces idempotency so a
/// double-click never creates a second customer or a second live subscription to the same plan.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private const string DefaultCurrencyCode = "USD";

    /// <summary>
    /// End-of-life states that do NOT count as an existing subscription for idempotency purposes,
    /// so a shopper can re-subscribe after their previous subscription ended. Every other state
    /// (active, trialing, past_due, on_hold, ...) is treated as "already subscribed".
    /// </summary>
    private static readonly HashSet<string> DeadStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    /// <summary>
    /// Per-(user, plan) gates that serialize the check-then-create critical section within this process,
    /// so a burst of concurrent subscribe requests cannot each create a subscription. Keyed by
    /// "{userName}|{planHandle}" — usernames are emails and plan handles are lower-case/hyphenated, so a
    /// pipe unambiguously separates them.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeGates = new();

    private readonly IMaxioApiClient _apiClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    internal MaxioBillingService(IMaxioApiClient apiClient, MaxioSettings settings, ILogger<MaxioBillingService> logger)
    {
        _apiClient = apiClient;
        _settings = settings;
        _logger = logger;
    }

    public MaxioBillingService(IMaxioApiClient apiClient, IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger)
        : this(apiClient, settings.Value, logger)
    {
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _apiClient.ListProductsForFamilyAsync(_settings.ProductFamilyHandle, cancellationToken);

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(ToSubscriptionPlan)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user identity is required to subscribe.", nameof(userName));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new SubscriptionPlanNotFoundException(planHandle ?? string.Empty);
        }

        // 1. Validate the plan exists in the configured family (case-sensitive, as Maxio handles are).
        var plans = await GetAvailablePlansAsync(cancellationToken);
        if (plans.All(plan => !string.Equals(plan.Handle, planHandle, StringComparison.Ordinal)))
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        // 2. Ensure a Maxio customer exists for this eShopOnWeb user (idempotent on reference = userName).
        var customer = await EnsureCustomerAsync(userName, cancellationToken);

        // 3. Serialize the check-then-create for this (user, plan) so a burst of concurrent requests
        //    (e.g. a double-click firing several times) collapses to a single subscription. Maxio's
        //    uniqueness_token only reliably dedupes sequential retries, not a simultaneous burst, so we
        //    guard the critical section in-process. NOTE: this guards within a single instance; a
        //    multi-instance deployment would additionally need a distributed lock. The Maxio-side
        //    uniqueness_token below still collapses cross-instance sequential retries.
        var gate = GetSubscribeGate(userName, planHandle);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Idempotency pre-check: if the shopper already has a live subscription to this plan, return it.
            var existing = await FindLiveSubscriptionForPlanAsync(customer.Id, planHandle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("User {User} already has live subscription {SubscriptionId} to plan {Plan}; returning existing.",
                    userName, existing.Id, planHandle);
                return new SubscriptionEnrollmentResult(ToCustomerSubscription(existing), alreadyExisted: true);
            }

            // Create the subscription. reference is unique (for traceability); uniqueness_token is
            // deterministic per (user, plan) as a second, cross-instance line of defence.
            var attributes = new MaxioSubscriptionAttributes
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                PaymentCollectionMethod = "remittance",
                Reference = BuildSubscriptionReference(userName, planHandle),
                UniquenessToken = BuildUniquenessToken(userName, planHandle)
            };

            try
            {
                var created = await _apiClient.CreateSubscriptionAsync(attributes, cancellationToken);
                _logger.LogInformation("Created subscription {SubscriptionId} ({State}) for user {User} on plan {Plan}.",
                    created.Id, created.State, userName, planHandle);
                return new SubscriptionEnrollmentResult(ToCustomerSubscription(created), alreadyExisted: false);
            }
            catch (MaxioApiException ex) when (ex.IsConflict || ex.IsUnprocessable)
            {
                // A concurrent request from another instance likely won the race and created it, or a
                // duplicate submission was rejected. Re-check; only treat as success if a live one now exists.
                _logger.LogWarning(ex, "Create subscription for user {User} on plan {Plan} returned {Status}; re-checking for an existing subscription.",
                    userName, planHandle, ex.StatusCode);

                var raced = await FindLiveSubscriptionForPlanAsync(customer.Id, planHandle, cancellationToken);
                if (raced is not null)
                {
                    return new SubscriptionEnrollmentResult(ToCustomerSubscription(raced), alreadyExisted: true);
                }

                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user identity is required.", nameof(userName));
        }

        var customer = await _apiClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            // The user has never subscribed, so no Maxio customer exists yet.
            return Array.Empty<CustomerSubscription>();
        }

        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(ToCustomerSubscription)
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var existing = await _apiClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var attributes = BuildCustomerAttributes(userName);
        try
        {
            var created = await _apiClient.CreateCustomerAsync(attributes, cancellationToken);
            _logger.LogInformation("Created Maxio customer {CustomerId} for user {User}.", created.Id, userName);
            return created;
        }
        catch (MaxioApiException ex) when (ex.IsUnprocessable)
        {
            // Reference must be unique: a concurrent request created the customer between our lookup
            // and create. Re-fetch and use the existing record.
            _logger.LogWarning("Customer create for {User} was rejected as duplicate; re-fetching existing customer.", userName);
            var raced = await _apiClient.FindCustomerByReferenceAsync(userName, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForPlanAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _apiClient.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.FirstOrDefault(subscription =>
            string.Equals(subscription.Product?.Handle, planHandle, StringComparison.Ordinal) &&
            IsLive(subscription.State));
    }

    private static SemaphoreSlim GetSubscribeGate(string userName, string planHandle) =>
        SubscribeGates.GetOrAdd($"{userName}|{planHandle}", _ => new SemaphoreSlim(1, 1));

    private static bool IsLive(string? state) =>
        !string.IsNullOrEmpty(state) && !DeadStates.Contains(state);

    private MaxioCustomerAttributes BuildCustomerAttributes(string userName)
    {
        // eShopOnWeb users are keyed by email (their username). Derive a reasonable display name from
        // the local part; both first and last name are required by Maxio.
        var atIndex = userName.IndexOf('@');
        var firstName = atIndex > 0 ? userName[..atIndex] : userName;
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = userName;
        }

        return new MaxioCustomerAttributes
        {
            FirstName = firstName,
            LastName = "(eShopOnWeb)",
            Email = userName,
            Reference = userName
        };
    }

    private static string BuildSubscriptionReference(string userName, string planHandle)
    {
        // Human-readable and traceable in the Maxio UI, with a short random suffix so the reference is
        // always unique (Maxio enforces subscription reference uniqueness permanently, even for
        // canceled subscriptions). Idempotency is enforced separately via the live-subscription
        // pre-check and the deterministic uniqueness_token.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var reference = $"eshopweb:{userName}:{planHandle}:{suffix}";
        return reference.Length <= 255 ? reference : reference[^255..];
    }

    private static string BuildUniquenessToken(string userName, string planHandle) =>
        $"eshopweb-subscribe:{userName}:{planHandle}";

    private static SubscriptionPlan ToSubscriptionPlan(MaxioProduct product) => new(
        productId: product.Id,
        handle: product.Handle!,
        name: product.Name ?? product.Handle!,
        description: product.Description,
        priceInCents: checked((int)product.PriceInCents),
        currencyCode: string.IsNullOrWhiteSpace(product.Currency) ? DefaultCurrencyCode : product.Currency!.ToUpperInvariant(),
        interval: product.Interval,
        intervalUnit: product.IntervalUnit ?? "month");

    private static CustomerSubscription ToCustomerSubscription(MaxioSubscription subscription) => new(
        id: subscription.Id,
        state: subscription.State ?? "unknown",
        customerId: subscription.Customer?.Id ?? 0,
        planHandle: subscription.Product?.Handle,
        planName: subscription.Product?.Name,
        priceInCents: checked((int)subscription.ProductPriceInCents),
        currencyCode: string.IsNullOrWhiteSpace(subscription.Product?.Currency) ? DefaultCurrencyCode : subscription.Product!.Currency!.ToUpperInvariant(),
        interval: subscription.Product?.Interval ?? 0,
        intervalUnit: subscription.Product?.IntervalUnit,
        currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        nextAssessmentAt: subscription.NextAssessmentAt,
        createdAt: subscription.CreatedAt,
        reference: subscription.Reference);
}
