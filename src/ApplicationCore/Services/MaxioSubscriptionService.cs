using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Outcome of a subscribe attempt.
/// </summary>
public class SubscribeResult
{
    public MaxioSubscription Subscription { get; init; } = default!;

    /// <summary>True when an existing subscription was returned instead of creating a new one (double-click / re-submit).</summary>
    public bool AlreadySubscribed { get; init; }
}

/// <summary>
/// Orchestrates the subscribe flow against Maxio, which is the billing system of record:
/// ensures the shopper has exactly one Maxio customer (idempotent via the unique customer
/// reference), then enrolls that customer in a plan with duplicate-safe semantics
/// (pre-check + Maxio uniqueness token + 409 recovery).
/// </summary>
public class MaxioSubscriptionService
{
    /// <summary>Prefix for the customer reference we store in Maxio, namespacing eShopOnWeb user ids.</summary>
    public const string CUSTOMER_REFERENCE_PREFIX = "eshoponweb:";

    public const string SUBSCRIPTION_REFERENCE_PREFIX = "eshoponweb:";

    /// <summary>
    /// States in which a subscription still occupies the shopper's seat on a plan
    /// (i.e. re-subscribing would create a duplicate rather than a new signup).
    /// </summary>
    private static readonly HashSet<string> ActiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "past_due", "soft_failure", "on_hold", "paused", "awaiting_signup", "assessing", "unpaid"
    };

    private readonly IMaxioBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioBillingClient client, MaxioSettings settings, IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public static string CustomerReferenceFor(string userId) => $"{CUSTOMER_REFERENCE_PREFIX}{userId}";

    public async Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        var familyHandle = GetConfiguredProductFamilyHandle();
        return await _client.ListPlansAsync(familyHandle, cancellationToken);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriberProfile profile, string planHandle, string? clientRequestId, CancellationToken cancellationToken = default)
    {
        var familyHandle = GetConfiguredProductFamilyHandle();

        // Validate the requested plan against the configured family so callers cannot enroll in
        // arbitrary products on the Maxio site.
        var plans = await _client.ListPlansAsync(familyHandle, cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new PlanNotOfferedException(planHandle);
        }

        var handle = plan.Handle ?? throw new MaxioIntegrationException($"The plan '{plan.Name}' has no handle and cannot be subscribed via the API.");

        var customer = await EnsureCustomerAsync(profile, cancellationToken);

        // Duplicate-safe enroll:
        // 1. Pre-check the customer's subscriptions for a live one on the same plan.
        var existing = await FindLiveSubscriptionForPlanAsync(customer.Id, handle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Shopper {CustomerReference} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId}).",
                profile.Reference, handle, existing.Id);
            return new SubscribeResult { Subscription = existing, AlreadySubscribed = true };
        }

        // 2. Still racing (double-click hits both pre-checks) — the uniqueness token makes Maxio
        //    reject the second POST with 409 within its 60-minute de-dup window. A caller-supplied
        //    request id scopes de-dup to one logical submit; otherwise we bucket by minute so a
        //    deliberate re-subscribe is possible shortly after.
        var uniquenessToken = BuildUniquenessToken(profile.Reference, handle, clientRequestId);
        var subscriptionReference = $"{SUBSCRIPTION_REFERENCE_PREFIX}{profile.Reference}:{handle}";

        try
        {
            var created = await _client.CreateSubscriptionAsync(customer.Id, handle, subscriptionReference, uniquenessToken, cancellationToken);
            _logger.LogInformation("Enrolled customer {CustomerReference} (Maxio customer {CustomerId}) in plan {PlanHandle} (Maxio subscription {SubscriptionId}).",
                profile.Reference, customer.Id, handle, created.Id);
            return new SubscribeResult { Subscription = created, AlreadySubscribed = false };
        }
        catch (MaxioApiException ex) when (ex.IsDuplicateSubmission)
        {
            // The first request of the duplicate pair succeeded; recover its result.
            _logger.LogWarning("Duplicate subscription submission detected for {CustomerReference} / {PlanHandle}; returning the already-created subscription.",
                profile.Reference, handle);

            var recovered = await FindLiveSubscriptionForPlanAsync(customer.Id, handle, cancellationToken)
                ?? throw new MaxioIntegrationException("The subscription request was processed as a duplicate, but the resulting subscription could not be retrieved. Please check your subscriptions.");

            return new SubscribeResult { Subscription = recovered, AlreadySubscribed = true };
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForSubscriberAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await GetSubscriptionsByCustomerReferenceAsync(CustomerReferenceFor(userId), cancellationToken);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsByCustomerReferenceAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        var customer = await _client.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        return await _client.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    /// <summary>
    /// Ensures exactly one Maxio customer exists for the shopper. Idempotent: the unique
    /// customer reference means a repeated call (or a lost response followed by a retry)
    /// converges on the same Maxio customer.
    /// </summary>
    public async Task<MaxioCustomer> EnsureCustomerAsync(SubscriberProfile profile, CancellationToken cancellationToken = default)
    {
        var existing = await _client.FindCustomerByReferenceAsync(profile.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _client.CreateCustomerAsync(profile, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity
                                        && ex.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase)))
        {
            // Lost a race against a concurrent create with the same reference: adopt the winner.
            var raced = await _client.FindCustomerByReferenceAsync(profile.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionForPlanAsync(long customerId, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _client.ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions
            .Where(s => string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase)
                     && ActiveStates.Contains(s.State))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    private static string BuildUniquenessToken(string customerReference, string planHandle, string? clientRequestId)
    {
        var scope = string.IsNullOrWhiteSpace(clientRequestId)
            ? DateTime.UtcNow.ToString("yyyyMMddHHmm")
            : clientRequestId.Trim();

        // Maxio requires uniqueness tokens of at least 20 characters; ours always exceed that.
        return $"eshop:{customerReference}:{planHandle}:{scope}";
    }

    private string GetConfiguredProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new MaxioIntegrationException($"'{MaxioSettings.SECTION_NAME}:{nameof(MaxioSettings.ProductFamilyHandle)}' is not configured.");
        }

        return _settings.ProductFamilyHandle.Trim();
    }
}

/// <summary>The requested plan is not part of the configured subscription catalog.</summary>
public class PlanNotOfferedException : Exception
{
    public PlanNotOfferedException(string planHandle)
        : base($"The plan '{planHandle}' is not available for subscription.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

