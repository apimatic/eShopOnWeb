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

public class SubscriptionService : ISubscriptionService
{
    // Subscription states in which the provider is still actively billing the subscription — i.e. not yet
    // paused/canceled/expired. Mirrors MaxioAdvancedBilling.Models.Enums.SubscriptionState wire values.
    private static readonly HashSet<string> BillingActiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "past_due", "assessing", "soft_failure", "unpaid", "trial_ended"
    };

    private readonly IBillingClient _billingClient;
    private readonly IPlanChangePreviewTokenService _previewTokenService;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient, IPlanChangePreviewTokenService previewTokenService, IPublisher publisher, IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _previewTokenService = previewTokenService;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default) =>
        _billingClient.ListPlansAsync(ct);

    public async Task<BillingSubscription> SubscribeAsync(string customerReference, string email, string firstName, string lastName, string planHandle, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        var customerId = await _billingClient.EnsureCustomerAsync(customerReference, email, firstName, lastName, ct);

        var existing = await _billingClient.FindActiveSubscriptionAsync(customerId, ct);
        if (existing is not null)
        {
            // Duplicate subscribe (double-click, repeated call): never create a second enrollment (UC1).
            return existing;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customerReference, planHandle, ct);

        await PublishBestEffortAsync(new SubscriptionActivated(customerReference, subscription.Id, subscription.ProductHandle), ct);

        return subscription;
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string customerReference, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));

        var customerId = await _billingClient.FindCustomerIdByReferenceAsync(customerReference, ct);
        if (customerId is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customerId.Value, ct);
    }

    public async Task<UsageRecord> RecordUsageAsync(string customerReference, int subscriptionId, int quantity, string? memo, bool isAdmin, CancellationToken ct = default)
    {
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, ct);

        if (!BillingActiveStates.Contains(subscription.State))
        {
            throw new InvalidSubscriptionStateException(
                $"Subscription {subscriptionId} is '{subscription.State}' and has no active billing period to record usage against.");
        }

        await _billingClient.ValidateMeteredComponentAsync(ct);

        var usage = await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, ct);

        // Read back the period-to-date total for the caller's convenience. Its failure does not fail the
        // whole operation — the usage record itself already stands (plan §UC2 failure scenarios).
        int? balance;
        try
        {
            balance = await _billingClient.GetMeteredUsageBalanceAsync(subscriptionId, ct);
        }
        catch (BillingProviderException)
        {
            balance = null;
        }

        return usage with { PeriodToDateBalance = balance };
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string customerReference, int subscriptionId, string targetPlanHandle, bool applyNow, bool isAdmin, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, ct);

        if (string.Equals(subscription.ProductHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException("The selected plan is already the subscription's current plan.");
        }

        EnsurePlanChangeIsLegal(subscription);

        var preview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, applyNow, ct);

        var payload = new PlanChangePreviewPayload(
            subscriptionId,
            subscription.CustomerReference,
            subscription.ProductHandle,
            targetPlanHandle,
            applyNow,
            preview.ProratedAdjustmentInCents,
            preview.ChargeInCents,
            preview.PaymentDueInCents,
            preview.CreditAppliedInCents,
            PlanChangePreviewTokenService.ComputeExpiry());

        var token = _previewTokenService.Protect(payload);

        return preview with { FromProductHandle = subscription.ProductHandle, PreviewToken = token };
    }

    public async Task<BillingSubscription> CommitPlanChangeAsync(string customerReference, string previewToken, bool isAdmin, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(previewToken, nameof(previewToken));

        if (!_previewTokenService.TryUnprotect(previewToken, out var payload) || payload is null)
        {
            throw new InvalidSubscriptionStateException("This plan change preview has expired or is invalid. Please request a new preview before confirming.");
        }

        if (!isAdmin && !string.Equals(payload.CustomerReference, customerReference, StringComparison.Ordinal))
        {
            throw new SubscriptionAccessDeniedException(payload.SubscriptionId);
        }

        var subscription = await _billingClient.GetSubscriptionAsync(payload.SubscriptionId, ct)
            ?? throw new SubscriptionNotFoundException(payload.SubscriptionId);

        EnsurePlanChangeIsLegal(subscription);

        // Re-price against the provider right before committing so the charge actually applied can never
        // silently drift from the amount previewed to the customer (plan §UC3 failure scenarios).
        var freshPreview = await _billingClient.PreviewPlanChangeAsync(payload.SubscriptionId, payload.ToProductHandle, payload.ApplyNow, ct);
        if (freshPreview.ProratedAdjustmentInCents != payload.ProratedAdjustmentInCents ||
            freshPreview.ChargeInCents != payload.ChargeInCents ||
            freshPreview.PaymentDueInCents != payload.PaymentDueInCents ||
            freshPreview.CreditAppliedInCents != payload.CreditAppliedInCents)
        {
            throw new InvalidSubscriptionStateException("The previewed amount is no longer current. Please request a new preview before confirming.");
        }

        var updated = await _billingClient.CommitPlanChangeAsync(payload.SubscriptionId, payload.ToProductHandle, payload.ApplyNow, ct);

        var effectiveAt = payload.ApplyNow ? DateTimeOffset.UtcNow : subscription.CurrentPeriodEndsAt ?? DateTimeOffset.UtcNow;
        await PublishBestEffortAsync(new SubscriptionPlanChanged(payload.CustomerReference, payload.SubscriptionId, payload.FromProductHandle, payload.ToProductHandle, effectiveAt), ct);

        return updated;
    }

    public async Task<BillingSubscription> PauseAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken ct = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, ct);

        if (string.Equals(subscription.State, "on_hold", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is already paused.");
        }
        if (!BillingActiveStates.Contains(subscription.State))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is '{subscription.State}' and cannot be paused.");
        }

        var updated = await _billingClient.PauseSubscriptionAsync(subscriptionId, ct);
        await PublishStateChangeAsync(subscription, updated, ct);
        return updated;
    }

    public async Task<BillingSubscription> ResumeAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken ct = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, ct);

        if (!string.Equals(subscription.State, "on_hold", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is '{subscription.State}', not paused, and cannot be resumed.");
        }

        var updated = await _billingClient.ResumeSubscriptionAsync(subscriptionId, ct);
        await PublishStateChangeAsync(subscription, updated, ct);
        return updated;
    }

    public async Task<BillingSubscription> CancelAsync(string customerReference, int subscriptionId, bool endOfPeriod, string? reason, bool isAdmin, CancellationToken ct = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, ct);

        if (string.Equals(subscription.State, "canceled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subscription.State, "expired", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is already '{subscription.State}'.");
        }

        if (endOfPeriod && subscription.DelayedCancelAt is not null)
        {
            // Already pending cancellation — surface the provider's existing outcome rather than
            // reporting this request as newly applied (UC4 failure scenarios).
            return subscription;
        }

        var updated = await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, reason, ct);
        await PublishStateChangeAsync(subscription, updated, ct);
        return updated;
    }

    public async Task<BillingSubscription> ReactivateAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken ct = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(customerReference, subscriptionId, isAdmin, ct);

        if (BillingActiveStates.Contains(subscription.State) || string.Equals(subscription.State, "on_hold", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException(
                $"Subscription {subscriptionId} is '{subscription.State}' and does not need to be reactivated.");
        }

        var updated = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, ct);
        await PublishStateChangeAsync(subscription, updated, ct);
        return updated;
    }

    private async Task<BillingSubscription> GetOwnedSubscriptionAsync(string customerReference, int subscriptionId, bool isAdmin, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(customerReference, nameof(customerReference));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, ct)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        if (!isAdmin && !string.Equals(subscription.CustomerReference, customerReference, StringComparison.Ordinal))
        {
            throw new SubscriptionAccessDeniedException(subscriptionId);
        }

        return subscription;
    }

    private static void EnsurePlanChangeIsLegal(BillingSubscription subscription)
    {
        if (!BillingActiveStates.Contains(subscription.State) && !string.Equals(subscription.State, "on_hold", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException(
                $"Subscription {subscription.Id} is '{subscription.State}' and is not eligible for a plan change. Reactivate it first.");
        }
    }

    private Task PublishStateChangeAsync(BillingSubscription before, BillingSubscription after, CancellationToken ct) =>
        PublishBestEffortAsync(new SubscriptionStateChanged(before.CustomerReference, before.Id, before.State, after.State), ct);

    /// <summary>
    /// Publishes best-effort, in-process only (plan §2.5): the provider call already succeeded by the time
    /// any of these are published, so a notification-handler failure must never roll back or surface as a
    /// failure of the use case itself — it is logged and swallowed here instead.
    /// </summary>
    private async Task PublishBestEffortAsync(INotification notification, CancellationToken ct)
    {
        try
        {
            await _publisher.Publish(notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to publish {NotificationType}: {Message}", notification.GetType().Name, ex.Message);
        }
    }
}
