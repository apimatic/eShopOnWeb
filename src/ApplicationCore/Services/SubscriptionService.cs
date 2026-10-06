using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscribe / list-my-subscriptions use cases against Maxio Advanced Billing.
///
/// Idempotency model (double-click safe, survives app restarts because Maxio enforces the anchors):
/// - one customer per eShop user, keyed by a deterministic reference "eshoponweb-user:{userId}"
///   (Maxio enforces unique references),
/// - one enrollment per user+plan keyed by "eshoponweb-sub:{userId}:{planHandle}"; a second create
///   attempt with the same reference is answered with the subscription Maxio already holds.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    public const string CUSTOMER_REFERENCE_PREFIX = "eshoponweb-user:";
    public const string SUBSCRIPTION_REFERENCE_PREFIX = "eshoponweb-sub:";

    private readonly IRepository<UserSubscription> _repository;
    private readonly IMaxioAdvancedBillingClient _maxio;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IRepository<UserSubscription> repository,
        IMaxioAdvancedBillingClient maxio,
        MaxioSettings settings,
        IAppLogger<SubscriptionService> logger)
    {
        _repository = repository;
        _maxio = maxio;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(_settings.ProductFamilyHandle, nameof(MaxioSettings.ProductFamilyHandle),
            "Maxio:ProductFamilyHandle is not configured.");

        var plans = await _maxio.GetPlansAsync(_settings.ProductFamilyHandle, cancellationToken);

        return plans
            .Where(plan => !plan.IsArchived)
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userId, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        userId = userId.Trim();
        planHandle = planHandle.Trim();

        var plans = await GetAvailablePlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        var customerReference = CUSTOMER_REFERENCE_PREFIX + userId;
        var subscriptionReference = $"{SUBSCRIPTION_REFERENCE_PREFIX}{userId}:{plan.Handle}";

        // 1. Ensure exactly one Maxio customer exists for this user (idempotent).
        var customer = await _maxio.EnsureCustomerAsync(
            customerReference,
            firstName: ToFirstName(userId),
            lastName: LastName,
            email: ToEmail(userId),
            cancellationToken: cancellationToken);

        // 2. Fast replay path: live mirror in our database (within a run) or live subscription at Maxio.
        var mirrored = (await _repository.ListAsync(new UserSubscriptionsSpecification(userId), cancellationToken))
            .FirstOrDefault(subscription => string.Equals(subscription.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));

        var liveAtProvider = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (liveAtProvider is not null && liveAtProvider.IsLive)
        {
            _logger.LogInformation($"User {userId} already has an active subscription {liveAtProvider.SubscriptionId} to plan {plan.Handle} - replaying it.");
            var replayed = await UpsertMirrorAsync(userId, customer, liveAtProvider, cancellationToken);
            return new SubscribeResult(replayed, Created: false);
        }

        // 2b. Full ledger check: a live enrollment for this plan may exist under a different
        //     (re-signed-up) reference, e.g. after an app restart lost the local mirror.
        var providerLedger = await _maxio.GetSubscriptionsForCustomerAsync(customer.CustomerId, cancellationToken);
        var liveForPlanElsewhere = providerLedger
            .FirstOrDefault(s => s.IsLive && string.Equals(s.PlanHandle, plan.Handle, StringComparison.OrdinalIgnoreCase));
        if (liveForPlanElsewhere is not null)
        {
            _logger.LogInformation($"User {userId} already has live subscription {liveForPlanElsewhere.SubscriptionId} to plan {plan.Handle} (reference {liveForPlanElsewhere.Reference}) - replaying it.");
            var replayedElsewhere = await UpsertMirrorAsync(userId, customer, liveForPlanElsewhere, cancellationToken);
            return new SubscribeResult(replayedElsewhere, Created: false);
        }

        // 3. First (or re-)enrollment. When the deterministic reference is already consumed by a
        //    cancelled subscription, suffix it so Maxio can accept the new signup.
        var effectiveReference = liveAtProvider is null
            ? subscriptionReference
            : $"{subscriptionReference}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        var subscription = await _maxio.SubscribeAsync(customer.CustomerId, plan.Handle, effectiveReference, cancellationToken)
            ?? // reference raced with a concurrent duplicate request: return what Maxio now holds
            await ResolveRacingReferenceAsync(effectiveReference, cancellationToken)
            ?? throw new MaxioApiException(502, new[] { "Maxio did not return the subscription after enrollment." }, null);

        var stored = await UpsertMirrorAsync(userId, customer, subscription, cancellationToken);
        _logger.LogInformation($"User {userId} subscribed to plan {plan.Handle}; Maxio subscription {subscription.SubscriptionId} is in state '{subscription.State}'.");
        return new SubscribeResult(stored, Created: true);
    }

    public async Task<IReadOnlyList<UserSubscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));

        var customer = await _maxio.FindCustomerByReferenceAsync(CUSTOMER_REFERENCE_PREFIX + userId.Trim(), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<UserSubscription>();
        }

        var providerSubscriptions = await _maxio.GetSubscriptionsForCustomerAsync(customer.CustomerId, cancellationToken);

        var results = new List<UserSubscription>();
        foreach (var providerSubscription in providerSubscriptions)
        {
            results.Add(await UpsertMirrorAsync(userId.Trim(), customer, providerSubscription, cancellationToken));
        }

        return results
            .OrderByDescending(subscription => subscription.CreatedAtUtc)
            .ThenByDescending(subscription => subscription.MaxioSubscriptionId)
            .ToList();
    }

    private async Task<MaxioSubscription?> ResolveRacingReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        return await _maxio.FindSubscriptionByReferenceAsync(reference, cancellationToken);
    }

    private async Task<UserSubscription> UpsertMirrorAsync(string userId, MaxioCustomer customer, MaxioSubscription subscription, CancellationToken cancellationToken)
    {
        var existing = (await _repository.ListAsync(new UserSubscriptionsSpecification(userId), cancellationToken))
            .FirstOrDefault(mirror => mirror.MaxioSubscriptionId == subscription.SubscriptionId);

        if (existing is not null)
        {
            existing.UpdateFrom(subscription);
            await _repository.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        var added = await _repository.AddAsync(new UserSubscription(userId, customer, subscription), cancellationToken);
        return added;
    }

    private const string LastName = "eShopOnWeb User";

    private static string ToFirstName(string userId)
    {
        var atIndex = userId.IndexOf('@');
        return atIndex > 0 ? userId[..atIndex] : userId;
    }

    private static string ToEmail(string userId)
    {
        return userId.Contains('@') ? userId : $"{userId}@eshoponweb.local";
    }
}
