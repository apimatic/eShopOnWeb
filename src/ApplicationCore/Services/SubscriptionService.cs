using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscription use cases (mirror <see cref="OrderService"/>): validates input and
/// state, drives the single <see cref="IBillingClient"/> seam, and publishes best-effort MediatR
/// notifications on state changes. Maxio is the system of record - there is no local persistence.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly IReadOnlyCollection<BillingSubscriptionState> MigratableStates = new[]
    {
        BillingSubscriptionState.Active,
        BillingSubscriptionState.Trialing,
    };

    private static readonly IReadOnlyCollection<BillingSubscriptionState> NonChangeableStates = new[]
    {
        BillingSubscriptionState.Canceled,
        BillingSubscriptionState.Expired,
        BillingSubscriptionState.FailedToCreate,
    };

    private static readonly IReadOnlyCollection<BillingSubscriptionState> ReactivatableStates = new[]
    {
        BillingSubscriptionState.Canceled,
        BillingSubscriptionState.Unpaid,
        BillingSubscriptionState.TrialEnded,
    };

    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher, IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListAvailablePlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        var profile = new BillingCustomerProfile(customerReference, email, firstName, lastName);
        var customer = await _billingClient.EnsureCustomerAsync(profile, cancellationToken);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = existingSubscriptions.FirstOrDefault(s => s.ProductHandle == planHandle && s.IsLive);
        if (existing is not null)
        {
            return new SubscribeResult(existing, wasAlreadyEnrolled: true);
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(customerReference, subscription.Id, planHandle), cancellationToken);

        return new SubscribeResult(subscription, wasAlreadyEnrolled: false);
    }

    public async Task<IReadOnlyList<BillingSubscription>> ListMySubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<BillingUsage> RecordUsageAsync(
        string customerReference,
        int subscriptionId,
        double quantity,
        string? memo,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Usage quantity must be positive.");
        }

        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, cancellationToken);
        if (subscription.State != BillingSubscriptionState.Active)
        {
            throw new InvalidSubscriptionStateException("record usage", subscription.State);
        }

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<BillingComponentBalance> GetUsageBalanceAsync(
        string customerReference,
        int subscriptionId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, cancellationToken);
        return await _billingClient.GetUsageBalanceAsync(subscriptionId, cancellationToken);
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeAsync(
        string customerReference,
        int subscriptionId,
        string targetPlanHandle,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, cancellationToken);
        EnsurePlanChangeIsPossible(subscription, targetPlanHandle);
        if (!MigratableStates.Contains(subscription.State))
        {
            throw new InvalidSubscriptionStateException("preview a plan change", subscription.State);
        }

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken);
    }

    public async Task<BillingSubscription> CommitPlanChangeAsync(
        string customerReference,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        BillingPlanChangePreview? confirmedPreview,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, cancellationToken);
        EnsurePlanChangeIsPossible(subscription, targetPlanHandle);

        var oldPlanHandle = subscription.ProductHandle;
        BillingSubscription updated;
        DateTimeOffset effectiveAt;

        if (timing == PlanChangeTiming.Now)
        {
            if (!MigratableStates.Contains(subscription.State))
            {
                throw new InvalidSubscriptionStateException("change plan now", subscription.State);
            }

            Guard.Against.Null(confirmedPreview, nameof(confirmedPreview));

            var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken);
            if (!freshPreview.MatchesAmounts(confirmedPreview))
            {
                throw new PlanChangePreviewStaleException();
            }

            updated = await _billingClient.CommitPlanChangeNowAsync(subscriptionId, targetPlanHandle, cancellationToken);
            effectiveAt = freshPreview.EffectiveAt;
        }
        else
        {
            if (NonChangeableStates.Contains(subscription.State))
            {
                throw new InvalidSubscriptionStateException("schedule a plan change", subscription.State);
            }

            updated = await _billingClient.SchedulePlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken);
            effectiveAt = updated.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow;
        }

        await PublishBestEffortAsync(new SubscriptionPlanChanged(customerReference, subscriptionId, oldPlanHandle, targetPlanHandle, effectiveAt), cancellationToken);

        return updated;
    }

    public async Task<BillingSubscription> PauseAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, cancellationToken);
        if (subscription.State != BillingSubscriptionState.Active && subscription.State != BillingSubscriptionState.Trialing)
        {
            throw new InvalidSubscriptionStateException("pause", subscription.State);
        }

        var updated = await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(customerReference, subscriptionId, subscription.State, updated.State, cancellationToken);
        return updated;
    }

    public async Task<BillingSubscription> ResumeAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, cancellationToken);
        if (subscription.State != BillingSubscriptionState.Paused)
        {
            throw new InvalidSubscriptionStateException("resume", subscription.State);
        }

        var updated = await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(customerReference, subscriptionId, subscription.State, updated.State, cancellationToken);
        return updated;
    }

    public async Task<BillingSubscription> CancelAsync(
        string customerReference,
        int subscriptionId,
        bool endOfPeriod,
        string? reason,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, cancellationToken);
        if (subscription.State == BillingSubscriptionState.Canceled || subscription.State == BillingSubscriptionState.Expired)
        {
            throw new InvalidSubscriptionStateException("cancel", subscription.State);
        }

        if (endOfPeriod && subscription.State == BillingSubscriptionState.PastDue)
        {
            throw new InvalidSubscriptionStateException("schedule an end-of-period cancellation", subscription.State);
        }

        var updated = await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, cancellationToken);
        await PublishStateChangeAsync(customerReference, subscriptionId, subscription.State, updated.State, cancellationToken);
        return updated;
    }

    public async Task<BillingSubscription> ReactivateAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, cancellationToken);
        if (!ReactivatableStates.Contains(subscription.State))
        {
            throw new InvalidSubscriptionStateException("reactivate", subscription.State);
        }

        var updated = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(customerReference, subscriptionId, subscription.State, updated.State, cancellationToken);
        return updated;
    }

    private async Task<BillingSubscription> GetOwnedSubscriptionAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!isAdmin && !string.Equals(subscription.CustomerReference, customerReference, StringComparison.Ordinal))
        {
            // Report "not found" rather than "forbidden" so a foreign subscription id cannot be
            // confirmed to exist by a non-owning caller.
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    private static void EnsurePlanChangeIsPossible(BillingSubscription subscription, string targetPlanHandle)
    {
        if (subscription.ProductHandle == targetPlanHandle)
        {
            throw new ArgumentException("Target plan must differ from the current plan.", nameof(targetPlanHandle));
        }
    }

    private async Task PublishStateChangeAsync(string customerReference, int subscriptionId, BillingSubscriptionState oldState, BillingSubscriptionState newState, CancellationToken cancellationToken)
    {
        await PublishBestEffortAsync(new SubscriptionStateChanged(customerReference, subscriptionId, oldState, newState), cancellationToken);
    }

    private async Task PublishBestEffortAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to publish {0} notification: {1}", notification.GetType().Name, ex.Message);
        }
    }
}
