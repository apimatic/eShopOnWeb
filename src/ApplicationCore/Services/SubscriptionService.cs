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
/// Orchestrates the subscription use cases (mirrors <see cref="OrderService"/>): validates,
/// drives the single <see cref="IBillingClient"/> seam, and publishes the corresponding
/// in-process MediatR notification after each successful state change (§2.5, best-effort).
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private const string AdminUserReference = "(admin)";

    private static readonly HashSet<SubscriptionState> TerminalStates = new()
    {
        SubscriptionState.Canceled,
        SubscriptionState.Expired,
        SubscriptionState.FailedToCreate
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

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default) =>
        _billingClient.ListPlansAsync(ct);

    public async Task<Subscription> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(productHandle, nameof(productHandle));

        var plan = await _billingClient.FindPlanByHandleAsync(productHandle, ct);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Configured plan handle '{productHandle}' does not resolve against the billing provider. Re-run UC0 seeding and update configuration.");
        }

        var resolvedFirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstNameFromEmail(email) : firstName;
        var resolvedLastName = string.IsNullOrWhiteSpace(lastName) ? "Customer" : lastName;

        var customerId = await _billingClient.GetOrCreateCustomerAsync(userReference, email, resolvedFirstName, resolvedLastName, ct);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customerId, ct);
        var alreadyEnrolled = existingSubscriptions.FirstOrDefault(s => !TerminalStates.Contains(s.State));
        if (alreadyEnrolled is not null)
        {
            // Duplicate subscribe (double-click, repeated call): never create a second enrollment.
            return alreadyEnrolled;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customerId, productHandle, ct);

        await PublishBestEffortAsync(
            () => _publisher.Publish(new SubscriptionActivated(userReference, subscription.Id, productHandle), ct),
            $"SubscriptionActivated for subscription {subscription.Id}");

        return subscription;
    }

    public async Task<IReadOnlyList<Subscription>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var customerId = await _billingClient.FindCustomerByReferenceAsync(userReference, ct);
        if (customerId is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customerId.Value, ct);
    }

    public async Task<Subscription> GetSubscriptionAsync(string? ownerReference, int subscriptionId, CancellationToken ct = default) =>
        await ResolveOwnedSubscriptionAsync(ownerReference, subscriptionId, ct);

    public async Task<UsageRecord> RecordUsageAsync(string? ownerReference, int subscriptionId, double quantity, string? memo, CancellationToken ct = default)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Usage quantity must be a positive number.", nameof(quantity));
        }

        var subscription = await ResolveOwnedSubscriptionAsync(ownerReference, subscriptionId, ct);
        if (!IsUsableForMetering(subscription.State))
        {
            throw new InvalidSubscriptionStateException(subscription.State.ToString(), "record usage");
        }

        await _billingClient.EnsureCatalogConfiguredAsync(ct);

        return await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, ct);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string? ownerReference, int subscriptionId, string targetProductHandle, PlanChangeTiming timing, CancellationToken ct = default)
    {
        var subscription = await ResolveOwnedSubscriptionAsync(ownerReference, subscriptionId, ct);
        return await BuildPreviewAsync(subscription, targetProductHandle, timing, ct);
    }

    public async Task<Subscription> CommitPlanChangeAsync(
        string? ownerReference,
        int subscriptionId,
        string targetProductHandle,
        PlanChangeTiming timing,
        int expectedProratedAdjustmentInCents,
        int expectedChargeInCents,
        CancellationToken ct = default)
    {
        var subscription = await ResolveOwnedSubscriptionAsync(ownerReference, subscriptionId, ct);

        var freshPreview = await BuildPreviewAsync(subscription, targetProductHandle, timing, ct);
        if (freshPreview.ProratedAdjustmentInCents != expectedProratedAdjustmentInCents ||
            freshPreview.ChargeInCents != expectedChargeInCents)
        {
            throw new StalePlanChangePreviewException(freshPreview);
        }

        var oldProductHandle = subscription.ProductHandle;
        var updated = timing == PlanChangeTiming.Immediate
            ? await _billingClient.CommitPlanChangeNowAsync(subscriptionId, targetProductHandle, ct)
            : await _billingClient.SchedulePlanChangeAtRenewalAsync(subscriptionId, targetProductHandle, ct);

        await PublishBestEffortAsync(
            () => _publisher.Publish(new SubscriptionPlanChanged(ownerReference ?? AdminUserReference, subscriptionId, oldProductHandle, targetProductHandle), ct),
            $"SubscriptionPlanChanged for subscription {subscriptionId}");

        return updated;
    }

    public async Task<Subscription> ApplyLifecycleActionAsync(string? ownerReference, int subscriptionId, SubscriptionLifecycleAction action, string? reason, CancellationToken ct = default)
    {
        var subscription = await ResolveOwnedSubscriptionAsync(ownerReference, subscriptionId, ct);

        if (!IsLegalTransition(subscription.State, action))
        {
            throw new InvalidSubscriptionStateException(subscription.State.ToString(), action.ToString());
        }

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause => await _billingClient.PauseSubscriptionAsync(subscriptionId, ct),
            SubscriptionLifecycleAction.Resume => await _billingClient.ResumeSubscriptionAsync(subscriptionId, ct),
            SubscriptionLifecycleAction.CancelImmediate => await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod: false, reason, ct),
            SubscriptionLifecycleAction.CancelAtEndOfPeriod => await _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod: true, reason, ct),
            SubscriptionLifecycleAction.Reactivate => await _billingClient.ReactivateSubscriptionAsync(subscriptionId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown lifecycle action.")
        };

        await PublishBestEffortAsync(
            () => _publisher.Publish(new SubscriptionStateChanged(ownerReference ?? AdminUserReference, subscriptionId, subscription.State, updated.State), ct),
            $"SubscriptionStateChanged for subscription {subscriptionId}");

        return updated;
    }

    public async Task RecordOrderPlacedUsageAsync(string userReference, CancellationToken ct = default)
    {
        try
        {
            var customerId = await _billingClient.FindCustomerByReferenceAsync(userReference, ct);
            if (customerId is null)
            {
                return;
            }

            var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customerId.Value, ct);
            var active = subscriptions.FirstOrDefault(s => IsUsableForMetering(s.State));
            if (active is null)
            {
                return;
            }

            await _billingClient.RecordUsageAsync(active.Id, quantity: 1, memo: "eShopOnWeb order placed", ct);
        }
        catch (Exception ex)
        {
            // Best-effort (§2.5): a Maxio failure must never roll back or block order placement.
            _logger.LogWarning("UC2 order-placed usage hook failed for user {0}: {1}", userReference, ex.Message);
        }
    }

    private async Task<PlanChangePreview> BuildPreviewAsync(Subscription subscription, string targetProductHandle, PlanChangeTiming timing, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(targetProductHandle, nameof(targetProductHandle));

        if (string.Equals(subscription.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The target plan is the same as the current plan.", nameof(targetProductHandle));
        }

        if (!IsPlanChangeable(subscription.State))
        {
            throw new InvalidSubscriptionStateException(subscription.State.ToString(), "change plan");
        }

        if (timing == PlanChangeTiming.Immediate)
        {
            var raw = await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetProductHandle, ct);
            return new PlanChangePreview(
                subscription.Id,
                subscription.ProductHandle,
                targetProductHandle,
                raw.ProratedAdjustmentInCents,
                raw.ChargeInCents,
                raw.PaymentDueInCents,
                raw.CreditAppliedInCents);
        }

        // At-next-renewal changes carry no proration (confirmed from SDK source, maxio-plan.md §2.5):
        // the customer is shown the new plan's price effective next period, nothing charged now.
        var targetPlan = await _billingClient.FindPlanByHandleAsync(targetProductHandle, ct);
        if (targetPlan is null)
        {
            throw new BillingConfigurationException(
                $"Configured plan handle '{targetProductHandle}' does not resolve against the billing provider. Re-run UC0 seeding and update configuration.");
        }

        return new PlanChangePreview(
            subscription.Id,
            subscription.ProductHandle,
            targetProductHandle,
            proratedAdjustmentInCents: 0,
            chargeInCents: targetPlan.PriceInCents,
            paymentDueInCents: 0,
            creditAppliedInCents: 0);
    }

    private async Task<Subscription> ResolveOwnedSubscriptionAsync(string? ownerReference, int subscriptionId, CancellationToken ct)
    {
        if (ownerReference is null)
        {
            // Admin caller: no ownership scoping.
            return await _billingClient.GetSubscriptionAsync(subscriptionId, ct);
        }

        var customerId = await _billingClient.FindCustomerByReferenceAsync(ownerReference, ct);
        if (customerId is null)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customerId.Value, ct);
        var owned = subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
        if (owned is null)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return owned;
    }

    private static bool IsUsableForMetering(SubscriptionState state) =>
        state is SubscriptionState.Active or SubscriptionState.Trialing;

    private static bool IsPlanChangeable(SubscriptionState state) =>
        !TerminalStates.Contains(state) && state != SubscriptionState.OnHold;

    private static bool IsLegalTransition(SubscriptionState state, SubscriptionLifecycleAction action) => action switch
    {
        SubscriptionLifecycleAction.Pause => !TerminalStates.Contains(state) && state != SubscriptionState.OnHold,
        SubscriptionLifecycleAction.Resume => state == SubscriptionState.OnHold,
        SubscriptionLifecycleAction.CancelImmediate => !TerminalStates.Contains(state),
        SubscriptionLifecycleAction.CancelAtEndOfPeriod => !TerminalStates.Contains(state),
        SubscriptionLifecycleAction.Reactivate => state is SubscriptionState.Canceled or SubscriptionState.Expired,
        _ => false
    };

    private static string DeriveFirstNameFromEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email[..atIndex] : email;
        return string.IsNullOrWhiteSpace(localPart) ? "Customer" : localPart;
    }

    private async Task PublishBestEffortAsync(Func<Task> publish, string description)
    {
        try
        {
            await publish();
        }
        catch (Exception ex)
        {
            // Best-effort in-process eventing (§2.5): the state change already stands; only log.
            _logger.LogWarning("Failed to publish {0}: {1}", description, ex.Message);
        }
    }
}
