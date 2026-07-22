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
/// Orchestrates the subscription use cases (mirrors <see cref="OrderService"/>): domain validation,
/// a single call through the <see cref="IBillingClient"/> seam, and a best-effort in-process MediatR
/// notification after the provider call succeeds (§2.5). It never talks HTTP directly.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient, IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<CustomerSubscription> SubscribeAsync(string userName, string productHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        // §4.4: the user reference (email/username) makes customer creation idempotent.
        var customer = await _billingClient.FindCustomerByReferenceAsync(userName, cancellationToken)
            ?? await _billingClient.CreateCustomerAsync(userName, userName, cancellationToken);

        // UC1 duplicate-subscribe guard: return an existing active subscription on this plan
        // rather than creating a second enrolment.
        var existing = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(s => s.IsActive
            && string.Equals(s.ProductHandle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (alreadyActive is not null)
        {
            _logger.LogInformation(
                $"User {userName} already has active subscription {alreadyActive.Id} on {productHandle}; returning it.");
            return alreadyActive;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, productHandle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(userName, subscription), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsForUserAsync(string userName,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<UsageResult> RecordUsageAsync(int subscriptionId, int quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        // Reject invalid input before any provider call (UC2 failure scenario).
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        // Startup / first-call validation: the configured component must resolve and be metered (UC2 precondition).
        var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);
        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Configured metered component '{component.Handle}' resolved to kind '{component.Kind}', not a metered component. Fix the sandbox seed (UC0) before recording usage.");
        }

        // The customer must have an active subscription (UC2 failure scenario) — nothing is sent otherwise.
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!subscription.IsActive)
        {
            throw new BillingProviderException(
                $"Subscription {subscriptionId} is in state '{subscription.State}'; usage can only be recorded against an active subscription.");
        }

        var recorded = await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);

        // Read back the running total; if that read fails the usage still stands (UC2 failure scenario).
        decimal? periodToDate;
        try
        {
            periodToDate = await _billingClient.GetUsageBalanceAsync(subscriptionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Recorded usage for subscription {subscriptionId} but failed to read back the period-to-date total: {ex.Message}");
            periodToDate = null;
        }

        return new UsageResult(recorded, periodToDate, component.UnitPrice);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle,
        bool applyImmediately, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        GuardPlanChangeAllowed(subscription, targetProductHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyImmediately, cancellationToken);
    }

    public async Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetProductHandle,
        bool applyImmediately, PlanChangePreview confirmedPreview, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));
        Guard.Against.Null(confirmedPreview, nameof(confirmedPreview));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        GuardPlanChangeAllowed(subscription, targetProductHandle);

        // Reject a commit whose preview has gone stale between preview and confirm (UC3 failure scenario).
        var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, applyImmediately, cancellationToken);
        if (!string.Equals(freshPreview.Signature, confirmedPreview.Signature, StringComparison.Ordinal))
        {
            throw new BillingProviderException(
                "The plan-change preview is no longer valid — the price or proration basis changed. Request a fresh preview and confirm again.");
        }

        var oldProductHandle = subscription.ProductHandle;
        var updated = await _billingClient.ChangePlanAsync(subscriptionId, targetProductHandle, applyImmediately, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(subscriptionId, oldProductHandle, targetProductHandle, updated), cancellationToken);

        return updated;
    }

    public Task<CustomerSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => TransitionAsync(subscriptionId,
            legal: s => s.IsActive,
            illegalMessage: s => $"Only an active subscription can be paused; subscription {subscriptionId} is '{s.State}'.",
            action: (id, ct) => _billingClient.PauseAsync(id, ct),
            cancellationToken);

    public Task<CustomerSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => TransitionAsync(subscriptionId,
            legal: s => string.Equals(s.State, "on_hold", StringComparison.OrdinalIgnoreCase),
            illegalMessage: s => $"Only a paused (on_hold) subscription can be resumed; subscription {subscriptionId} is '{s.State}'.",
            action: (id, ct) => _billingClient.ResumeAsync(id, ct),
            cancellationToken);

    public Task<CustomerSubscription> CancelAsync(int subscriptionId, bool immediate, string? reason,
        CancellationToken cancellationToken = default)
        => TransitionAsync(subscriptionId,
            legal: s => !string.Equals(s.State, "canceled", StringComparison.OrdinalIgnoreCase),
            illegalMessage: s => $"Subscription {subscriptionId} is already canceled.",
            action: (id, ct) => _billingClient.CancelAsync(id, immediate, reason, ct),
            cancellationToken);

    public Task<CustomerSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => TransitionAsync(subscriptionId,
            legal: s => string.Equals(s.State, "canceled", StringComparison.OrdinalIgnoreCase),
            illegalMessage: s => $"Only a canceled subscription can be reactivated; subscription {subscriptionId} is '{s.State}'.",
            action: (id, ct) => _billingClient.ReactivateAsync(id, ct),
            cancellationToken);

    private async Task<CustomerSubscription> TransitionAsync(int subscriptionId,
        Func<CustomerSubscription, bool> legal,
        Func<CustomerSubscription, string> illegalMessage,
        Func<int, CancellationToken, Task<CustomerSubscription>> action,
        CancellationToken cancellationToken)
    {
        // Local legal-transition check (UC4): reject illegal transitions before any provider call.
        var current = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!legal(current))
        {
            throw new BillingProviderException(illegalMessage(current));
        }

        var oldState = current.State;
        var updated = await action(subscriptionId, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(subscriptionId, oldState, updated.State, updated), cancellationToken);

        return updated;
    }

    private static void GuardPlanChangeAllowed(CustomerSubscription subscription, string targetProductHandle)
    {
        // Reject a no-op change to the current plan before any provider call (UC3 failure scenario).
        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingProviderException(
                $"Subscription {subscription.Id} is already on plan '{targetProductHandle}'.");
        }

        // Migrations require an active/trialing subscription (UC3 failure scenario).
        if (!subscription.IsActive)
        {
            throw new BillingProviderException(
                $"Subscription {subscription.Id} is in state '{subscription.State}' and cannot change plan. Reactivate it first (UC4).");
        }
    }

    private async Task PublishBestEffortAsync(INotification notification, CancellationToken cancellationToken)
    {
        // Best-effort, in-process only (§2.5): a handler failure never rolls back the provider action.
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"In-process notification {notification.GetType().Name} failed to publish/handle: {ex.Message}");
        }
    }
}
