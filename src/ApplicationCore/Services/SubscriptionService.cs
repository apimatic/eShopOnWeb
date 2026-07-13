using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscription use cases (UC1-UC4): validates input, drives the single billing-provider
/// seam (<see cref="IBillingClient"/>), and publishes best-effort in-process MediatR notifications on
/// successful state changes. Mirrors <c>OrderService</c>'s role as the use-case surface over its provider
/// abstraction. Runs stateless per §8 of the integration plan: the eShopOnWeb user reference is the
/// idempotent key into Maxio, so no local persistence is required or maintained.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> InactiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private static readonly HashSet<string> PausedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "on_hold", "paused"
    };

    private readonly IBillingClient _billingClient;
    private readonly IPlanChangePreviewCache _previewCache;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(5);

    public SubscriptionService(
        IBillingClient billingClient,
        IPlanChangePreviewCache previewCache,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _previewCache = previewCache;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var plans = await _billingClient.ListPlansAsync(ct);
        return plans.Select(ToDto).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userReference)) throw new ArgumentException("A user reference is required.", nameof(userReference));
        if (string.IsNullOrWhiteSpace(productHandle)) throw new ArgumentException("A product handle is required.", nameof(productHandle));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, ct)
                       ?? await _billingClient.CreateCustomerAsync(userReference, email, firstName, lastName, ct);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
        var alreadyActive = existingSubscriptions.FirstOrDefault(s => IsActiveish(s.State));
        if (alreadyActive != null)
        {
            // Duplicate subscribe (double-click, repeated call): never create a second enrollment.
            return ToDto(alreadyActive);
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, productHandle, ct);

        await PublishBestEffort(new SubscriptionActivated(userReference, subscription.Id, productHandle), ct);

        return ToDto(subscription);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForUserAsync(string userReference, CancellationToken ct = default)
    {
        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, ct);
        if (customer == null) return Array.Empty<SubscriptionDto>();

        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
        return subscriptions.Select(ToDto).ToList();
    }

    public async Task<UsageResultDto> RecordUsageAsync(int subscriptionId, string requestingUserReference, bool isAdmin, double quantity, string? memo, CancellationToken ct = default)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Usage quantity must be a positive number.");

        var subscription = await LoadAuthorizedAsync(subscriptionId, requestingUserReference, isAdmin, ct);
        if (!IsActiveish(subscription.State))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is not active (state: {subscription.State}); usage cannot be recorded.");
        }

        // Confirms the configured metered component still resolves to a metered-kind component before
        // any usage is sent (UC2 precondition / first-call validation).
        await _billingClient.GetMeteredUsageComponentAsync(ct);

        var usage = await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, ct);
        var periodToDate = await _billingClient.TryGetComponentPeriodToDateUsageAsync(subscriptionId, ct);

        return new UsageResultDto(usage.Id, usage.QuantityRecorded, periodToDate, periodToDate.HasValue);
    }

    public async Task<PlanChangePreviewDto> PreviewPlanChangeAsync(int subscriptionId, string requestingUserReference, bool isAdmin, string targetProductHandle, bool applyAtRenewal, CancellationToken ct = default)
    {
        var subscription = await LoadAuthorizedAsync(subscriptionId, requestingUserReference, isAdmin, ct);

        if (!IsActiveish(subscription.State))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is in state '{subscription.State}' and cannot change plans; reactivate it first.");
        }

        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is already on plan '{targetProductHandle}'.");
        }

        var targetPlan = await _billingClient.GetPlanByHandleAsync(targetProductHandle, ct);
        if (targetPlan == null)
        {
            throw new ArgumentException($"Target plan handle '{targetProductHandle}' does not resolve. Check the Maxio product configuration (UC0).", nameof(targetProductHandle));
        }

        BillingPlanChangePreview preview = applyAtRenewal
            ? new BillingPlanChangePreview(null, null, null, null)
            : await _billingClient.PreviewPlanChangeNowAsync(subscriptionId, targetProductHandle, ct);

        var fromHandle = subscription.ProductHandle ?? string.Empty;
        var expiresAt = DateTimeOffset.UtcNow.Add(PreviewLifetime);
        var token = _previewCache.Store(new PlanChangePreviewEntry(subscriptionId, fromHandle, targetProductHandle, applyAtRenewal, expiresAt));

        return new PlanChangePreviewDto(
            token,
            subscriptionId,
            fromHandle,
            targetProductHandle,
            applyAtRenewal,
            preview.ProratedAdjustmentInCents,
            preview.ChargeInCents,
            preview.PaymentDueInCents,
            preview.CreditAppliedInCents,
            targetPlan.PriceInCents,
            expiresAt);
    }

    public async Task<SubscriptionDto> CommitPlanChangeAsync(int subscriptionId, string requestingUserReference, bool isAdmin, Guid previewToken, CancellationToken ct = default)
    {
        var entry = _previewCache.TryConsume(previewToken);
        if (entry == null || entry.SubscriptionId != subscriptionId)
        {
            throw new StalePlanChangePreviewException();
        }

        // Re-read the subscription's current state from the provider before committing: if it drifted
        // since the preview was issued, reject rather than silently applying a different amount.
        var subscription = await LoadAuthorizedAsync(subscriptionId, requestingUserReference, isAdmin, ct);
        if (!string.Equals(subscription.ProductHandle, entry.FromProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new StalePlanChangePreviewException();
        }

        var committed = entry.ApplyAtRenewal
            ? await _billingClient.SchedulePlanChangeAtRenewalAsync(subscriptionId, entry.ToProductHandle, ct)
            : await _billingClient.CommitPlanChangeNowAsync(subscriptionId, entry.ToProductHandle, ct);

        await PublishBestEffort(new SubscriptionPlanChanged(requestingUserReference, subscriptionId, entry.FromProductHandle, entry.ToProductHandle, entry.ApplyAtRenewal), ct);

        return ToDto(committed);
    }

    public async Task<SubscriptionDto> ChangeLifecycleStateAsync(int subscriptionId, string requestingUserReference, bool isAdmin, SubscriptionLifecycleAction action, bool endOfPeriod, string? reason, CancellationToken ct = default)
    {
        var subscription = await LoadAuthorizedAsync(subscriptionId, requestingUserReference, isAdmin, ct);
        EnsureLegalTransition(subscription, action);

        var result = action switch
        {
            SubscriptionLifecycleAction.Pause => await _billingClient.PauseAsync(subscriptionId, ct),
            SubscriptionLifecycleAction.Resume => await _billingClient.ResumeAsync(subscriptionId, ct),
            SubscriptionLifecycleAction.Cancel => endOfPeriod
                ? await _billingClient.CancelAtEndOfPeriodAsync(subscriptionId, reason, ct)
                : await _billingClient.CancelNowAsync(subscriptionId, reason, ct),
            SubscriptionLifecycleAction.Reactivate => await _billingClient.ReactivateAsync(subscriptionId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        await PublishBestEffort(new SubscriptionStateChanged(requestingUserReference, subscriptionId, subscription.State, result.State), ct);

        return ToDto(result);
    }

    private async Task<BillingSubscription> LoadAuthorizedAsync(int subscriptionId, string requestingUserReference, bool isAdmin, CancellationToken ct)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, ct);

        if (!isAdmin && !string.Equals(subscription.CustomerReference, requestingUserReference, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionAccessDeniedException(subscriptionId);
        }

        return subscription;
    }

    private static void EnsureLegalTransition(BillingSubscription subscription, SubscriptionLifecycleAction action)
    {
        var isPaused = PausedStates.Contains(subscription.State);
        var isInactive = InactiveStates.Contains(subscription.State);

        var legal = action switch
        {
            SubscriptionLifecycleAction.Pause => !isPaused && !isInactive,
            SubscriptionLifecycleAction.Resume => isPaused,
            SubscriptionLifecycleAction.Cancel => !isInactive,
            SubscriptionLifecycleAction.Reactivate => isInactive,
            _ => false
        };

        if (!legal)
        {
            throw new InvalidSubscriptionStateException($"Cannot {action} subscription {subscription.Id} while it is in state '{subscription.State}'.");
        }
    }

    private async Task PublishBestEffort(INotification notification, CancellationToken ct)
    {
        try
        {
            // Cast to object so this always binds to IPublisher's non-generic overload, regardless of
            // the notification's static type here — keeps dispatch deterministic (and easy to assert
            // in tests) rather than depending on which Publish<T> overload the compiler infers.
            await _publisher.Publish((object)notification, ct);
        }
        catch (Exception ex)
        {
            // Best-effort in-process eventing (§2.5): the provider-side change already succeeded and
            // must stand; a handler failure is logged, never rolled back or surfaced as the operation's error.
            _logger.LogWarning("Failed to publish {NotificationType}: {Error}", notification.GetType().Name, ex.Message);
        }
    }

    private static bool IsActiveish(string state) => !InactiveStates.Contains(state);

    private static SubscriptionDto ToDto(BillingSubscription s) => new(
        s.Id,
        s.ProductHandle,
        s.ProductName,
        s.State,
        s.ProductPriceInCents,
        s.CurrentPeriodEndsAt,
        s.NextAssessmentAt,
        s.DelayedCancelAt);

    private static SubscriptionPlanDto ToDto(BillingPlan p) => new(p.Handle, p.Name, p.PriceInCents, p.IntervalCount, p.IntervalUnit);
}
