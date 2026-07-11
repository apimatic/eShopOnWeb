using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

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

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string buyerId, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var existing = await _billingClient.FindSubscriptionByCustomerReferenceAsync(buyerId, cancellationToken);
        if (existing is not null && !existing.IsCanceled)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(buyerId);
        await _billingClient.EnsureCustomerAsync(buyerId, buyerId, firstName, lastName, cancellationToken);

        var subscription = await _billingClient.CreateSubscriptionAsync(buyerId, productHandle, cancellationToken);

        await PublishSafelyAsync(new SubscriptionActivated(subscription.Id, buyerId, productHandle), cancellationToken);

        return subscription;
    }

    public Task<Subscription?> GetMySubscriptionAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return _billingClient.FindSubscriptionByCustomerReferenceAsync(buyerId, cancellationToken);
    }

    public async Task<(UsageRecord Usage, UsagePeriodSummary Summary)> RecordUsageAsync(string actingBuyerId, bool isAdmin, int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await GetOwnedSubscriptionAsync(actingBuyerId, isAdmin, subscriptionId, cancellationToken);
        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionTransitionException(subscription.State, "record usage against");
        }

        var usage = await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
        var summary = await _billingClient.GetUsagePeriodToDateAsync(subscriptionId, cancellationToken);

        return (usage, summary);
    }

    public async Task<UsagePeriodSummary> GetUsageSummaryAsync(string actingBuyerId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        await GetOwnedSubscriptionAsync(actingBuyerId, isAdmin, subscriptionId, cancellationToken);
        return await _billingClient.GetUsagePeriodToDateAsync(subscriptionId, cancellationToken);
    }

    public async Task RecordOrderPlacedUsageAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscription = await _billingClient.FindSubscriptionByCustomerReferenceAsync(buyerId, cancellationToken);
            if (subscription is null || !subscription.IsActive)
            {
                return;
            }

            await _billingClient.RecordUsageAsync(subscription.Id, 1, "Order placed", cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort: an order has already been placed successfully; a usage-recording failure must
            // never affect the order lifecycle (plan.md §2.5).
            _logger.LogWarning("Failed to record automatic order-placed usage for {0}: {1}", buyerId, ex.Message);
        }
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string actingBuyerId, bool isAdmin, int subscriptionId, string targetProductHandle, bool immediate, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        var subscription = await GetOwnedSubscriptionAsync(actingBuyerId, isAdmin, subscriptionId, cancellationToken);
        EnsurePlanChangeIsLegal(subscription, targetProductHandle);

        var preview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, immediate, cancellationToken);
        return WithCommitToken(preview);
    }

    public async Task<Subscription> CommitPlanChangeAsync(string actingBuyerId, bool isAdmin, int subscriptionId, string targetProductHandle, bool immediate, string commitToken, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));
        Guard.Against.NullOrEmpty(commitToken, nameof(commitToken));

        var subscription = await GetOwnedSubscriptionAsync(actingBuyerId, isAdmin, subscriptionId, cancellationToken);
        EnsurePlanChangeIsLegal(subscription, targetProductHandle);

        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, immediate, cancellationToken);
        var freshToken = ComputeCommitToken(subscriptionId, targetProductHandle, immediate, freshPreview);
        if (!string.Equals(freshToken, commitToken, StringComparison.Ordinal))
        {
            throw new PlanChangeException("The previewed cost is no longer current - request a fresh preview before committing this plan change.");
        }

        var updated = await _billingClient.CommitPlanChangeAsync(subscriptionId, targetProductHandle, immediate, cancellationToken);

        await PublishSafelyAsync(new SubscriptionPlanChanged(subscriptionId, subscription.CustomerReference, subscription.ProductHandle, targetProductHandle, immediate), cancellationToken);

        return updated;
    }

    public async Task<Subscription> PauseAsync(string actingBuyerId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(actingBuyerId, isAdmin, subscriptionId, cancellationToken);
        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionTransitionException(subscription.State, "pause");
        }

        var updated = await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishSafelyAsync(new SubscriptionStateChanged(subscriptionId, subscription.CustomerReference, subscription.State, updated.State), cancellationToken);
        return updated;
    }

    public async Task<Subscription> ResumeAsync(string actingBuyerId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(actingBuyerId, isAdmin, subscriptionId, cancellationToken);
        if (!subscription.IsPaused)
        {
            throw new InvalidSubscriptionTransitionException(subscription.State, "resume");
        }

        var updated = await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishSafelyAsync(new SubscriptionStateChanged(subscriptionId, subscription.CustomerReference, subscription.State, updated.State), cancellationToken);
        return updated;
    }

    public async Task<Subscription> CancelAsync(string actingBuyerId, bool isAdmin, int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(actingBuyerId, isAdmin, subscriptionId, cancellationToken);
        if (subscription.IsCanceled)
        {
            throw new InvalidSubscriptionTransitionException(subscription.State, "cancel");
        }

        var updated = await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, cancellationToken);
        await PublishSafelyAsync(new SubscriptionStateChanged(subscriptionId, subscription.CustomerReference, subscription.State, updated.State), cancellationToken);
        return updated;
    }

    public async Task<Subscription> ReactivateAsync(string actingBuyerId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(actingBuyerId, isAdmin, subscriptionId, cancellationToken);
        if (!subscription.IsCanceled)
        {
            throw new InvalidSubscriptionTransitionException(subscription.State, "reactivate");
        }

        var updated = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishSafelyAsync(new SubscriptionStateChanged(subscriptionId, subscription.CustomerReference, subscription.State, updated.State), cancellationToken);
        return updated;
    }

    private async Task<Subscription> GetOwnedSubscriptionAsync(string actingBuyerId, bool isAdmin, int subscriptionId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(actingBuyerId, nameof(actingBuyerId));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!isAdmin && !string.Equals(subscription.CustomerReference, actingBuyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionAccessDeniedException(subscriptionId);
        }

        return subscription;
    }

    private static void EnsurePlanChangeIsLegal(Subscription subscription, string targetProductHandle)
    {
        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new PlanChangeException("The subscription is already on the requested plan.");
        }

        if (subscription.IsCanceled)
        {
            throw new InvalidSubscriptionTransitionException(subscription.State, "change the plan of");
        }
    }

    private static PlanChangePreview WithCommitToken(PlanChangePreview preview)
    {
        var token = ComputeCommitToken(preview.SubscriptionId, preview.TargetProductHandle, preview.Immediate, preview);
        return new PlanChangePreview(
            preview.SubscriptionId,
            preview.CurrentProductHandle,
            preview.TargetProductHandle,
            preview.Immediate,
            preview.ProratedAdjustmentInCents,
            preview.ChargeInCents,
            preview.PaymentDueInCents,
            preview.CreditAppliedInCents,
            token);
    }

    /// <summary>
    /// A stable, opaque token binding a commit request to the exact amounts previewed - if the provider's
    /// pricing basis changes between preview and commit, the freshly computed token will differ and the
    /// commit is rejected rather than silently applying a different amount than the one shown (UC3).
    /// </summary>
    private static string ComputeCommitToken(int subscriptionId, string targetProductHandle, bool immediate, PlanChangePreview preview)
    {
        return string.Join(
            ':',
            subscriptionId.ToString(CultureInfo.InvariantCulture),
            targetProductHandle,
            immediate,
            preview.ProratedAdjustmentInCents,
            preview.ChargeInCents,
            preview.PaymentDueInCents,
            preview.CreditAppliedInCents);
    }

    private async Task PublishSafelyAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort in-process eventing (plan.md §2.5): the provider call already succeeded, so a
            // handler failure is logged only and never rolls back the subscription change.
            _logger.LogWarning("Failed to publish {0}: {1}", notification.GetType().Name, ex.Message);
        }
    }

    private static (string FirstName, string LastName) SplitDisplayName(string buyerReference)
    {
        var atIndex = buyerReference.IndexOf('@');
        var localPart = atIndex > 0 ? buyerReference[..atIndex] : buyerReference;
        return (localPart, "eShopOnWeb Customer");
    }
}
