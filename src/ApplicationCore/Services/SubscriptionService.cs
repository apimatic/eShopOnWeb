using System;
using System.Collections.Generic;
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
/// Orchestrates subscription use cases (mirrors <see cref="OrderService"/>): validates the
/// requested transition against the subscription's current state, drives the single
/// <see cref="IBillingClient"/> seam, and publishes the corresponding MediatR notification after a
/// successful provider call. Notification publication is best-effort: a handler failure is logged
/// and never rolls back or masks a successful billing operation (§2.5).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly SubscriptionState[] UsableForUsageStates = { SubscriptionState.Active, SubscriptionState.Trialing };
    private static readonly SubscriptionState[] ChangeablePlanStates = { SubscriptionState.Active, SubscriptionState.Trialing, SubscriptionState.PastDue };
    private static readonly SubscriptionState[] PausableStates = { SubscriptionState.Active, SubscriptionState.Trialing };
    private static readonly SubscriptionState[] ResumableStates = { SubscriptionState.Paused, SubscriptionState.OnHold };
    private static readonly SubscriptionState[] ReactivatableStates = { SubscriptionState.Canceled, SubscriptionState.Expired, SubscriptionState.PastDue };

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
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userName, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var customer = await _billingClient.EnsureCustomerAsync(userName, email, cancellationToken);

        var existing = await _billingClient.FindActiveSubscriptionAsync(customer.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);

        await PublishSafelyAsync(
            new SubscriptionActivated(subscription.Id, userName, subscription.ProductHandle, subscription.PriceInCents, subscription.NextAssessmentAt),
            cancellationToken);

        return subscription;
    }

    public async Task<Subscription?> FindSubscriptionForUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        return customer is null ? null : await _billingClient.FindLatestSubscriptionAsync(customer.Id, cancellationToken);
    }

    public Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default) =>
        _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

    public async Task<BillingUsageBalance> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        RequireState(subscription, UsableForUsageStates, "record usage against");

        await _billingClient.EnsureMeteredComponentIsValidAsync(cancellationToken);

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<BillingProrationPreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        ValidatePlanChangeTarget(subscription, targetProductHandle);

        return await BuildPreviewAsync(subscriptionId, targetProductHandle, applyNow, cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId, string targetProductHandle, bool applyNow, BillingProrationPreview expectedPreview, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));
        Guard.Against.Null(expectedPreview, nameof(expectedPreview));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        ValidatePlanChangeTarget(subscription, targetProductHandle);

        var freshPreview = await BuildPreviewAsync(subscriptionId, targetProductHandle, applyNow, cancellationToken);
        if (freshPreview.ChargeInCents != expectedPreview.ChargeInCents ||
            freshPreview.ProratedAdjustmentInCents != expectedPreview.ProratedAdjustmentInCents ||
            freshPreview.PaymentDueInCents != expectedPreview.PaymentDueInCents)
        {
            throw new StalePreviewException(
                $"The previewed cost for changing subscription {subscriptionId} to '{targetProductHandle}' has changed; request a fresh preview before confirming.");
        }

        var oldProductHandle = subscription.ProductHandle;
        var updated = applyNow
            ? await _billingClient.MigratePlanNowAsync(subscriptionId, targetProductHandle, cancellationToken)
            : await _billingClient.SchedulePlanChangeAsync(subscriptionId, targetProductHandle, cancellationToken);

        await PublishSafelyAsync(
            new SubscriptionPlanChanged(updated.Id, updated.UserName, oldProductHandle, targetProductHandle, applyNow, updated.NextAssessmentAt ?? DateTimeOffset.UtcNow),
            cancellationToken);

        return updated;
    }

    public async Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var before = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        RequireState(before, PausableStates, "pause");

        var after = await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(before, after, cancellationToken);
        return after;
    }

    public async Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var before = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        RequireState(before, ResumableStates, "resume");

        var after = await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(before, after, cancellationToken);
        return after;
    }

    public async Task<Subscription> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var before = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (before.State is SubscriptionState.Canceled or SubscriptionState.Expired)
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is already {before.State}.");
        }

        var after = await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, cancellationToken);
        await PublishStateChangeAsync(before, after, cancellationToken);
        return after;
    }

    public async Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var before = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        RequireState(before, ReactivatableStates, "reactivate");

        var after = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken);
        await PublishStateChangeAsync(before, after, cancellationToken);
        return after;
    }

    private async Task<BillingProrationPreview> BuildPreviewAsync(int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken)
    {
        if (applyNow)
        {
            return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, cancellationToken);
        }

        // No provider preview endpoint exists for the delayed/next-renewal path (no proration applies
        // there); compose the preview from the target plan's known price instead (maxio-plan.md, Capability 5).
        var targetPlan = await _billingClient.GetPlanAsync(targetProductHandle, cancellationToken);
        return new BillingProrationPreview(
            TargetProductHandle: targetProductHandle,
            AppliesNow: false,
            ProratedAdjustmentInCents: 0,
            ChargeInCents: targetPlan.PriceInCents,
            PaymentDueInCents: 0,
            CreditAppliedInCents: 0);
    }

    private static void ValidatePlanChangeTarget(Subscription subscription, string targetProductHandle)
    {
        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscription.Id} is already on plan '{targetProductHandle}'.");
        }

        RequireState(subscription, ChangeablePlanStates, "change the plan on");
    }

    private static void RequireState(Subscription subscription, SubscriptionState[] allowedStates, string action)
    {
        if (Array.IndexOf(allowedStates, subscription.State) < 0)
        {
            throw new InvalidSubscriptionStateException(
                $"Cannot {action} subscription {subscription.Id} while it is in state '{subscription.State}'. Allowed states: {string.Join(", ", allowedStates)}.");
        }
    }

    private async Task PublishStateChangeAsync(Subscription before, Subscription after, CancellationToken cancellationToken)
    {
        await PublishSafelyAsync(
            new SubscriptionStateChanged(after.Id, after.UserName, before.State, after.State, DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async Task PublishSafelyAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("In-process handler for {NotificationType} failed: {Message}", notification.GetType().Name, ex.Message);
        }
    }
}
