using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    private static readonly BillingSubscriptionState[] OpenStates =
    {
        BillingSubscriptionState.Active,
        BillingSubscriptionState.Trialing
    };

    // Process-wide, not per-request: the metered-component configuration only needs confirming
    // once per process lifetime ("again before the first usage call" — UC2 preconditions), not on
    // every call. A health check separately confirms it at service startup.
    private static volatile bool _meteredComponentConfigurationValidated;
    private static readonly SemaphoreSlim ConfigurationValidationLock = new(1, 1);

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

    public async Task<BillingSubscription> SubscribeAsync(string userReference, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(email, nameof(email));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        var customer = await _billingClient.EnsureCustomerAsync(userReference, email, cancellationToken);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existingOpenSubscription = existingSubscriptions.FirstOrDefault(s => OpenStates.Contains(s.State));
        if (existingOpenSubscription is not null)
        {
            // Duplicate subscribe (double-click, repeated call): never create a second enrollment.
            return existingOpenSubscription;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        await PublishSafelyAsync(new SubscriptionActivated(userReference, subscription.Id, subscription.PlanHandle), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<BillingSubscription> GetSubscriptionForUserAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (!isAdmin)
        {
            await EnsureOwnershipAsync(userReference, subscription, cancellationToken);
        }

        return subscription;
    }

    public async Task<BillingUsageRecordResult> RecordUsageAsync(string userReference, int subscriptionId, int quantity, string? memo, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new SubscriptionValidationException("Usage quantity must be greater than zero.");
        }

        await EnsureMeteredComponentConfigurationValidatedAsync(cancellationToken);

        var subscription = await GetSubscriptionForUserAsync(userReference, subscriptionId, isAdmin, cancellationToken);
        if (!OpenStates.Contains(subscription.State))
        {
            throw new SubscriptionValidationException($"Subscription {subscriptionId} has no active subscription to bill usage against (current state: {subscription.State}).");
        }

        await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, cancellationToken);

        int? balance;
        try
        {
            balance = await _billingClient.GetMeteredUsageBalanceAsync(subscriptionId, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            // The usage was recorded; a failed read-back must not fail the whole operation (§UC2).
            _logger.LogWarning("Usage was recorded for subscription {SubscriptionId} but reading back the period-to-date balance failed: {Message}", subscriptionId, ex.Message);
            balance = null;
        }

        return new BillingUsageRecordResult
        {
            RecordedQuantity = quantity,
            PeriodToDateBalance = balance
        };
    }

    public async Task<int> GetUsageBalanceAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await GetSubscriptionForUserAsync(userReference, subscriptionId, isAdmin, cancellationToken);
        return await _billingClient.GetMeteredUsageBalanceAsync(subscriptionId, cancellationToken);
    }

    public async Task<BillingPlanChangePreview> PreviewPlanChangeAsync(string userReference, int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionForUserAsync(userReference, subscriptionId, isAdmin: false, cancellationToken: cancellationToken);
        ValidatePlanChangeRequest(subscription, targetPlanHandle);

        return await ComputePreviewAsync(subscription, targetPlanHandle, applyNow, cancellationToken);
    }

    public async Task<BillingSubscription> CommitPlanChangeAsync(string userReference, int subscriptionId, string targetPlanHandle, bool applyNow, int? expectedProratedAdjustmentInCents, CancellationToken cancellationToken = default)
    {
        var subscription = await GetSubscriptionForUserAsync(userReference, subscriptionId, isAdmin: false, cancellationToken: cancellationToken);
        ValidatePlanChangeRequest(subscription, targetPlanHandle);

        var freshPreview = await ComputePreviewAsync(subscription, targetPlanHandle, applyNow, cancellationToken);
        if (freshPreview.ProratedAdjustmentInCents != expectedProratedAdjustmentInCents)
        {
            throw new SubscriptionValidationException("The previewed amount is no longer current; request a fresh preview before committing.");
        }

        var oldPlanHandle = subscription.PlanHandle;
        var updated = applyNow
            ? await _billingClient.CommitPlanChangeNowAsync(subscriptionId, targetPlanHandle, cancellationToken)
            : await _billingClient.SchedulePlanChangeAtRenewalAsync(subscriptionId, targetPlanHandle, cancellationToken);

        await PublishSafelyAsync(new SubscriptionPlanChanged(userReference, subscriptionId, oldPlanHandle, targetPlanHandle, applyNow, freshPreview.EffectiveDate), cancellationToken);

        return updated;
    }

    public Task<BillingSubscription> PauseAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(userReference, subscriptionId, isAdmin,
            legalFrom: OpenStates,
            transition: (client, id, ct) => client.PauseSubscriptionAsync(id, ct),
            cancellationToken);

    public Task<BillingSubscription> ResumeAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(userReference, subscriptionId, isAdmin,
            legalFrom: new[] { BillingSubscriptionState.Paused },
            transition: (client, id, ct) => client.ResumeSubscriptionAsync(id, ct),
            cancellationToken);

    public Task<BillingSubscription> CancelAsync(string userReference, int subscriptionId, bool endOfPeriod, bool isAdmin, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(userReference, subscriptionId, isAdmin,
            legalFrom: OpenStates.Concat(new[] { BillingSubscriptionState.Paused, BillingSubscriptionState.PastDue }).ToArray(),
            transition: (client, id, ct) => client.CancelSubscriptionAsync(id, endOfPeriod, ct),
            cancellationToken);

    public Task<BillingSubscription> ReactivateAsync(string userReference, int subscriptionId, bool isAdmin, CancellationToken cancellationToken = default) =>
        ApplyTransitionAsync(userReference, subscriptionId, isAdmin,
            legalFrom: new[] { BillingSubscriptionState.Cancelled, BillingSubscriptionState.Expired },
            transition: (client, id, ct) => client.ReactivateSubscriptionAsync(id, ct),
            cancellationToken);

    private async Task<BillingSubscription> ApplyTransitionAsync(
        string userReference,
        int subscriptionId,
        bool isAdmin,
        IReadOnlyCollection<BillingSubscriptionState> legalFrom,
        Func<IBillingClient, int, CancellationToken, Task<BillingSubscription>> transition,
        CancellationToken cancellationToken)
    {
        var subscription = await GetSubscriptionForUserAsync(userReference, subscriptionId, isAdmin, cancellationToken);
        if (!legalFrom.Contains(subscription.State))
        {
            throw new SubscriptionValidationException(
                $"Subscription {subscriptionId} cannot transition from its current state ({subscription.State}). Legal source states: {string.Join(", ", legalFrom)}.");
        }

        var oldState = subscription.State;
        var updated = await transition(_billingClient, subscriptionId, cancellationToken);

        await PublishSafelyAsync(new SubscriptionStateChanged(userReference, subscriptionId, oldState, updated.State), cancellationToken);

        return updated;
    }

    private async Task<BillingPlanChangePreview> ComputePreviewAsync(BillingSubscription subscription, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken)
    {
        if (applyNow)
        {
            return await _billingClient.PreviewPlanChangeNowAsync(subscription.Id, targetPlanHandle, cancellationToken);
        }

        // No provider operation previews the delayed ("at next renewal") path; compose it from
        // already-known data instead (target plan price + the subscription's own renewal date).
        var plans = await _billingClient.ListPlansAsync(cancellationToken);
        var targetPlan = plans.FirstOrDefault(p => p.Handle == targetPlanHandle)
            ?? throw new SubscriptionValidationException($"Target plan handle '{targetPlanHandle}' does not resolve.");

        var effectiveDate = subscription.NextBillingDate
            ?? throw new SubscriptionValidationException($"Subscription {subscription.Id} has no known renewal date to schedule the change against.");

        return new BillingPlanChangePreview
        {
            TargetPlanHandle = targetPlan.Handle,
            Prorated = false,
            EffectiveDate = effectiveDate,
            ProratedAdjustmentInCents = null
        };
    }

    private void ValidatePlanChangeRequest(BillingSubscription subscription, string targetPlanHandle)
    {
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionValidationException("The target plan is the same as the subscription's current plan.");
        }

        if (!OpenStates.Contains(subscription.State))
        {
            throw new SubscriptionValidationException($"Subscription {subscription.Id} is not in a state that allows a plan change (current state: {subscription.State}). Reactivate it first.");
        }
    }

    private async Task EnsureMeteredComponentConfigurationValidatedAsync(CancellationToken cancellationToken)
    {
        if (_meteredComponentConfigurationValidated)
        {
            return;
        }

        await ConfigurationValidationLock.WaitAsync(cancellationToken);
        try
        {
            if (_meteredComponentConfigurationValidated)
            {
                return;
            }

            await _billingClient.EnsureConfigurationValidAsync(cancellationToken);
            _meteredComponentConfigurationValidated = true;
        }
        finally
        {
            ConfigurationValidationLock.Release();
        }
    }

    private async Task EnsureOwnershipAsync(string userReference, BillingSubscription subscription, CancellationToken cancellationToken)
    {
        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null || customer.Id != subscription.BillingCustomerId)
        {
            throw new SubscriptionValidationException($"Subscription {subscription.Id} does not belong to the current user.");
        }
    }

    private async Task PublishSafelyAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort, in-process eventing only (§2.5): a handler failure never rolls back
            // the subscription change that already succeeded against the billing provider.
            _logger.LogWarning("Failed to publish {NotificationType}: {Message}", notification.GetType().Name, ex.Message);
        }
    }
}
