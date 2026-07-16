using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates UC1-UC4 (mirrors <see cref="OrderService"/>): validates the request, drives the single
/// <see cref="IBillingClient"/> seam, and publishes best-effort MediatR notifications on success (§2.5).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher, IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default) =>
        _billingClient.ListPlansAsync(ct);

    public async Task<Subscription> SubscribeAsync(string userId, string userEmail, string productHandle, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Guard.Against.NullOrWhiteSpace(userEmail, nameof(userEmail));
        Guard.Against.NullOrWhiteSpace(productHandle, nameof(productHandle));

        var customer = await _billingClient.EnsureCustomerAsync(userId, userEmail, ct);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
        var current = existingSubscriptions.FirstOrDefault(s => !IsTerminal(s.State));
        if (current is not null)
        {
            _logger.LogInformation(
                "User {0} already has subscription {1} in state {2}; returning it instead of creating a new enrollment.",
                userId, current.Id, current.State);
            return current;
        }

        var created = await _billingClient.CreateSubscriptionAsync(customer.Id, productHandle, ct);

        await PublishBestEffortAsync(
            new SubscriptionActivated(userId, created.Id, created.ProductHandle, created.PriceInCents, created.NextAssessmentAt), ct);

        return created;
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userId, ct);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
    }

    public async Task<Subscription> GetSubscriptionAsync(string userId, int subscriptionId, bool isAdmin, CancellationToken ct = default) =>
        await GetOwnedSubscriptionAsync(userId, subscriptionId, isAdmin, ct);

    public async Task<UsageRecordResult> RecordUsageAsync(string userId, int subscriptionId, int quantity, string? memo, bool isAdmin, CancellationToken ct = default)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await GetOwnedSubscriptionAsync(userId, subscriptionId, isAdmin, ct);
        if (IsTerminal(subscription.State))
        {
            throw new InvalidSubscriptionStateException(subscriptionId, subscription.State, "record usage on");
        }

        var component = await _billingClient.GetMeteredComponentAsync(ct);
        return await _billingClient.RecordUsageAsync(subscriptionId, component.Id, quantity, memo, ct);
    }

    public async Task RecordAutomaticUsageAsync(string userId, int quantity, string memo, CancellationToken ct = default)
    {
        try
        {
            var customer = await _billingClient.FindCustomerByReferenceAsync(userId, ct);
            if (customer is null)
            {
                return; // no billing customer for this user yet - nothing to meter
            }

            var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
            var active = subscriptions.FirstOrDefault(s => !IsTerminal(s.State));
            if (active is null)
            {
                return; // no active subscription - automatic usage is a no-op, not an error (UC2 trigger, §8)
            }

            var component = await _billingClient.GetMeteredComponentAsync(ct);
            await _billingClient.RecordUsageAsync(active.Id, component.Id, quantity, memo, ct);
        }
        catch (BillingProviderException ex)
        {
            // Best-effort: a Maxio failure here must never block eShopOnWeb's order lifecycle.
            _logger.LogWarning("Automatic usage recording skipped for user {0}: {1}", userId, ex.Message);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userId, int subscriptionId, string targetProductHandle, bool applyImmediately, bool isAdmin, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(targetProductHandle, nameof(targetProductHandle));

        var subscription = await GetOwnedSubscriptionAsync(userId, subscriptionId, isAdmin, ct);
        EnsurePlanChangeAllowed(subscription, targetProductHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyImmediately, ct);
    }

    public async Task<Subscription> CommitPlanChangeAsync(string userId, int subscriptionId, string targetProductHandle, bool applyImmediately, PlanChangePreview expectedPreview, bool isAdmin, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(targetProductHandle, nameof(targetProductHandle));
        Guard.Against.Null(expectedPreview, nameof(expectedPreview));

        var subscription = await GetOwnedSubscriptionAsync(userId, subscriptionId, isAdmin, ct);
        EnsurePlanChangeAllowed(subscription, targetProductHandle);

        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyImmediately, ct);
        if (freshPreview != expectedPreview)
        {
            throw new StalePlanChangePreviewException(subscriptionId, expectedPreview, freshPreview);
        }

        var oldProductHandle = subscription.ProductHandle;
        var updated = await _billingClient.CommitPlanChangeAsync(subscriptionId, targetProductHandle, applyImmediately, ct);

        var effectiveAt = applyImmediately ? DateTimeOffset.UtcNow : updated.NextAssessmentAt ?? DateTimeOffset.UtcNow;
        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(userId, subscriptionId, oldProductHandle, targetProductHandle, freshPreview.ProratedAdjustmentInCents, effectiveAt), ct);

        return updated;
    }

    public Task<Subscription> PauseAsync(string userId, int subscriptionId, bool isAdmin, CancellationToken ct = default) =>
        ExecuteTransitionAsync(userId, subscriptionId, isAdmin, "pause",
            current => current.State is SubscriptionState.Paused or SubscriptionState.OnHold or SubscriptionState.Canceled
                or SubscriptionState.Expired or SubscriptionState.FailedToCreate,
            (id, c) => _billingClient.PauseSubscriptionAsync(id, c), ct);

    public Task<Subscription> ResumeAsync(string userId, int subscriptionId, bool isAdmin, CancellationToken ct = default) =>
        ExecuteTransitionAsync(userId, subscriptionId, isAdmin, "resume",
            current => current.State is not (SubscriptionState.Paused or SubscriptionState.OnHold),
            (id, c) => _billingClient.ResumeSubscriptionAsync(id, c), ct);

    public Task<Subscription> CancelAsync(string userId, int subscriptionId, bool cancelAtEndOfPeriod, string? reason, bool isAdmin, CancellationToken ct = default) =>
        ExecuteTransitionAsync(userId, subscriptionId, isAdmin, "cancel",
            current => current.State is SubscriptionState.Canceled or SubscriptionState.Expired or SubscriptionState.FailedToCreate,
            (id, c) => _billingClient.CancelSubscriptionAsync(id, cancelAtEndOfPeriod, reason, c), ct);

    public Task<Subscription> ReactivateAsync(string userId, int subscriptionId, bool isAdmin, CancellationToken ct = default) =>
        ExecuteTransitionAsync(userId, subscriptionId, isAdmin, "reactivate",
            current => current.State is not (SubscriptionState.Canceled or SubscriptionState.Expired),
            (id, c) => _billingClient.ReactivateSubscriptionAsync(id, c), ct);

    private async Task<Subscription> ExecuteTransitionAsync(
        string userId,
        int subscriptionId,
        bool isAdmin,
        string action,
        Func<Subscription, bool> isIllegalFromCurrentState,
        Func<int, CancellationToken, Task<Subscription>> transition,
        CancellationToken ct)
    {
        var current = await GetOwnedSubscriptionAsync(userId, subscriptionId, isAdmin, ct);
        if (isIllegalFromCurrentState(current))
        {
            throw new InvalidSubscriptionStateException(subscriptionId, current.State, action);
        }

        try
        {
            var updated = await transition(subscriptionId, ct);
            await PublishBestEffortAsync(
                new SubscriptionStateChanged(userId, subscriptionId, current.State, updated.State, DateTimeOffset.UtcNow), ct);
            return updated;
        }
        catch (BillingProviderException)
        {
            // The provider rejected a transition our local check allowed - state drifted out-of-band
            // (dunning, an admin action in the Maxio UI; there are no webhooks, §7). Treat the provider's
            // state as truth and surface the conflict instead of retrying blindly.
            var refreshed = await _billingClient.GetSubscriptionAsync(subscriptionId, ct);
            throw new InvalidSubscriptionStateException(subscriptionId, refreshed.State, action);
        }
    }

    private async Task<Subscription> GetOwnedSubscriptionAsync(string userId, int subscriptionId, bool isAdmin, CancellationToken ct)
    {
        Guard.Against.NullOrWhiteSpace(userId, nameof(userId));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, ct);
        if (!isAdmin && !string.Equals(subscription.CustomerReference, userId, StringComparison.OrdinalIgnoreCase))
        {
            // Reported as "not found" rather than "forbidden" so a customer can't probe for other users' subscription ids.
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    private static void EnsurePlanChangeAllowed(Subscription subscription, string targetProductHandle)
    {
        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException(subscription.Id, subscription.State, "change to the plan it is already on");
        }

        if (IsTerminal(subscription.State))
        {
            throw new InvalidSubscriptionStateException(subscription.Id, subscription.State, "change the plan of");
        }
    }

    private static bool IsTerminal(SubscriptionState state) =>
        state is SubscriptionState.Canceled or SubscriptionState.Expired or SubscriptionState.FailedToCreate;

    private async Task PublishBestEffortAsync(INotification notification, CancellationToken ct)
    {
        try
        {
            await _publisher.Publish(notification, ct);
        }
        catch (Exception ex)
        {
            // Best-effort in-process eventing (§2.5): a handler failure must never fail the subscription action itself.
            _logger.LogWarning("Notification {0} did not fully deliver: {1}", notification.GetType().Name, ex.Message);
        }
    }
}
