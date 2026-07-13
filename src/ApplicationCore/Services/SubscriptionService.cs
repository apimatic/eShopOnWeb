using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

// Orchestrates the billing client + eShopOnWeb-side rules (ownership, legal transitions,
// idempotency) and publishes MediatR notifications on state changes (mirrors OrderService).
public class SubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> ActiveLikeStates = new(StringComparer.OrdinalIgnoreCase) { "active", "trialing" };
    private static readonly HashSet<string> ReactivatableStates = new(StringComparer.OrdinalIgnoreCase) { "canceled", "unpaid", "trial_ended" };

    private readonly IBillingClient _billingClient;
    private readonly ISubscriptionCatalogOptions _catalogOptions;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IBillingClient billingClient,
        ISubscriptionCatalogOptions catalogOptions,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _catalogOptions = catalogOptions;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userReference, string email, string productHandle, CancellationToken cancellationToken = default)
    {
        EnsureKnownProductHandle(productHandle);

        var customer = await EnsureCustomerAsync(userReference, email, cancellationToken);

        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(s => ActiveLikeStates.Contains(s.State));
        if (alreadyActive is not null)
        {
            // Duplicate subscribe: never create a second enrollment, return the existing one (§ UC1 failure scenarios).
            return ToEntity(alreadyActive);
        }

        var created = await _billingClient.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);

        await _publisher.Publish(new SubscriptionActivated(created.Id, userReference, created.ProductHandle, created.ProductName, created.PriceInCents), cancellationToken);

        return ToEntity(created);
    }

    public async Task<IReadOnlyList<Subscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default)
    {
        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        var subscriptions = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToEntity).ToList();
    }

    public async Task<SubscriptionUsageResult> RecordUsageAsync(string actingUserReference, bool isAdmin, int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Usage quantity must be a positive number.");
        }

        var component = await _billingClient.GetComponentAsync(_catalogOptions.MeteredComponentHandle, cancellationToken);
        if (!component.IsMetered)
        {
            throw new InvalidSubscriptionStateException(
                $"Configured component '{_catalogOptions.MeteredComponentHandle}' is not a metered component (kind: {component.Kind}). Fix the seed (UC0) before recording usage.");
        }

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureAccessible(subscription, actingUserReference, isAdmin);

        if (!ActiveLikeStates.Contains(subscription.State))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} has no active billing (current state: {subscription.State}).");
        }

        await _billingClient.RecordUsageAsync(subscriptionId, _catalogOptions.MeteredComponentHandle, quantity, memo, cancellationToken);

        int? total;
        try
        {
            total = await _billingClient.GetComponentUnitBalanceAsync(subscriptionId, _catalogOptions.MeteredComponentHandle, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            // The usage stands; report success with the total marked unavailable (§ UC2 failure scenarios).
            _logger.LogWarning("Usage was recorded for subscription {0} but reading back the period-to-date total failed: {1}", subscriptionId, ex.Message);
            total = null;
        }

        return new SubscriptionUsageResult(quantity, total);
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeAsync(string userReference, int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        EnsureKnownProductHandle(targetProductHandle);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureAccessible(subscription, userReference, isAdmin: false);
        EnsurePlanChangeIsLegal(subscription, targetProductHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, cancellationToken);
    }

    public async Task<Subscription> ChangePlanNowAsync(string userReference, int subscriptionId, string targetProductHandle, BillingPlanChangePreview confirmedPreview, CancellationToken cancellationToken = default)
    {
        EnsureKnownProductHandle(targetProductHandle);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureAccessible(subscription, userReference, isAdmin: false);
        EnsurePlanChangeIsLegal(subscription, targetProductHandle);

        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, cancellationToken);
        if (freshPreview.ProratedAdjustmentInCents != confirmedPreview.ProratedAdjustmentInCents ||
            freshPreview.ChargeInCents != confirmedPreview.ChargeInCents ||
            freshPreview.PaymentDueInCents != confirmedPreview.PaymentDueInCents ||
            freshPreview.CreditAppliedInCents != confirmedPreview.CreditAppliedInCents)
        {
            // Never silently apply a different amount than the one previewed (§ UC3 failure scenarios).
            throw new StalePlanPreviewException();
        }

        var oldProductHandle = subscription.ProductHandle;
        var updated = await ExecuteTransitionAsync(subscriptionId,
            () => _billingClient.ChangePlanNowAsync(subscriptionId, targetProductHandle, cancellationToken),
            cancellationToken);

        await _publisher.Publish(new SubscriptionPlanChanged(subscriptionId, userReference, oldProductHandle, targetProductHandle, appliedNow: true, freshPreview.ProratedAdjustmentInCents), cancellationToken);

        return ToEntity(updated);
    }

    public async Task<Subscription> SchedulePlanChangeAsync(string userReference, int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default)
    {
        EnsureKnownProductHandle(targetProductHandle);

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureAccessible(subscription, userReference, isAdmin: false);
        EnsurePlanChangeIsLegal(subscription, targetProductHandle);

        var oldProductHandle = subscription.ProductHandle;
        var updated = await ExecuteTransitionAsync(subscriptionId,
            () => _billingClient.SchedulePlanChangeAsync(subscriptionId, targetProductHandle, cancellationToken),
            cancellationToken);

        await _publisher.Publish(new SubscriptionPlanChanged(subscriptionId, userReference, oldProductHandle, targetProductHandle, appliedNow: false, prorationAmountInCents: null), cancellationToken);

        return ToEntity(updated);
    }

    public async Task<Subscription> PauseAsync(string actingUserReference, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureAccessible(subscription, actingUserReference, isAdmin);

        if (!ActiveLikeStates.Contains(subscription.State))
        {
            throw new InvalidSubscriptionStateException($"Cannot pause subscription {subscriptionId}: current state is '{subscription.State}', expected active or trialing.");
        }

        return await TransitionAndPublishAsync(actingUserReference, subscriptionId, subscription.State,
            () => _billingClient.PauseAsync(subscriptionId, cancellationToken), cancellationToken);
    }

    public async Task<Subscription> ResumeAsync(string actingUserReference, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureAccessible(subscription, actingUserReference, isAdmin);

        if (!string.Equals(subscription.State, "on_hold", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException($"Cannot resume subscription {subscriptionId}: current state is '{subscription.State}', expected on_hold.");
        }

        return await TransitionAndPublishAsync(actingUserReference, subscriptionId, subscription.State,
            () => _billingClient.ResumeAsync(subscriptionId, cancellationToken), cancellationToken);
    }

    public async Task<Subscription> CancelAsync(string actingUserReference, bool isAdmin, int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureAccessible(subscription, actingUserReference, isAdmin);

        if (string.Equals(subscription.State, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is already canceled.");
        }

        if (endOfPeriod && subscription.CancelAtEndOfPeriod)
        {
            // Surface the provider's outcome rather than reporting the request as newly applied (§ UC4 failure scenarios).
            throw new InvalidSubscriptionStateException($"Subscription {subscriptionId} is already scheduled to cancel at the end of its current period.");
        }

        var oldState = subscription.State;
        var updated = await TransitionAndPublishAsync(actingUserReference, subscriptionId, oldState,
            () => endOfPeriod
                ? _billingClient.CancelAtEndOfPeriodAsync(subscriptionId, reason, cancellationToken)
                : _billingClient.CancelNowAsync(subscriptionId, reason, cancellationToken),
            cancellationToken,
            newStateOverride: endOfPeriod ? "pending_cancellation" : null);

        return updated;
    }

    public async Task<Subscription> ReactivateAsync(string actingUserReference, bool isAdmin, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        EnsureAccessible(subscription, actingUserReference, isAdmin);

        if (!ReactivatableStates.Contains(subscription.State))
        {
            throw new InvalidSubscriptionStateException(
                $"Cannot reactivate subscription {subscriptionId}: current state is '{subscription.State}'. Legal states are canceled, unpaid, or trial_ended.");
        }

        return await TransitionAndPublishAsync(actingUserReference, subscriptionId, subscription.State,
            () => _billingClient.ReactivateAsync(subscriptionId, cancellationToken), cancellationToken);
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(string userReference, string email, CancellationToken cancellationToken)
    {
        var existing = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var localPart = email.Split('@')[0];
        return await _billingClient.CreateCustomerAsync(userReference, email, firstName: localPart, lastName: "Customer", cancellationToken);
    }

    private void EnsureKnownProductHandle(string productHandle)
    {
        if (!string.Equals(productHandle, _catalogOptions.DefaultProductHandle, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(productHandle, _catalogOptions.AlternateProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{productHandle}' is not one of the configured plans ({_catalogOptions.DefaultProductHandle}, {_catalogOptions.AlternateProductHandle}).", nameof(productHandle));
        }
    }

    private static void EnsurePlanChangeIsLegal(BillingSubscription subscription, string targetProductHandle)
    {
        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionStateException($"Subscription {subscription.Id} is already on plan '{targetProductHandle}'.");
        }

        if (!ActiveLikeStates.Contains(subscription.State))
        {
            throw new InvalidSubscriptionStateException(
                $"Cannot change plan for subscription {subscription.Id}: current state is '{subscription.State}'. Reactivate the subscription first.");
        }
    }

    private static void EnsureAccessible(BillingSubscription subscription, string actingUserReference, bool isAdmin)
    {
        if (isAdmin)
        {
            return;
        }

        if (!string.Equals(subscription.CustomerReference, actingUserReference, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionNotFoundException(subscription.Id);
        }
    }

    private Subscription ToEntity(BillingSubscription s) => new(
        s.Id,
        s.CustomerReference ?? string.Empty,
        s.CustomerId,
        s.ProductHandle,
        s.ProductName,
        s.PriceInCents,
        s.State,
        s.CurrentPeriodEndsAt,
        s.NextAssessmentAt,
        s.CancelAtEndOfPeriod);

    // Runs a lifecycle transition; if the provider rejects it (state drifted out-of-band), re-reads the
    // subscription and surfaces the conflict with the provider's authoritative state (§ UC4 failure scenarios).
    private async Task<BillingSubscription> ExecuteTransitionAsync(int subscriptionId, Func<Task<BillingSubscription>> transition, CancellationToken cancellationToken)
    {
        try
        {
            return await transition();
        }
        catch (BillingProviderException ex)
        {
            var refreshed = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
            throw new InvalidSubscriptionStateException($"Billing provider reports subscription {subscriptionId} is currently '{refreshed.State}': {ex.Message}");
        }
    }

    private async Task<Subscription> TransitionAndPublishAsync(
        string userReference,
        int subscriptionId,
        string oldState,
        Func<Task<BillingSubscription>> transition,
        CancellationToken cancellationToken,
        string? newStateOverride = null)
    {
        var updated = await ExecuteTransitionAsync(subscriptionId, transition, cancellationToken);

        await _publisher.Publish(new SubscriptionStateChanged(subscriptionId, userReference, oldState, newStateOverride ?? updated.State), cancellationToken);

        return ToEntity(updated);
    }
}
