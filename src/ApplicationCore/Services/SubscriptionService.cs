using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class SubscriptionService : ISubscriptionService
{
    private static readonly HashSet<string> TerminalStates = new()
    {
        SubscriptionStates.Canceled, SubscriptionStates.Expired, SubscriptionStates.FailedToCreate
    };

    private static readonly HashSet<string> PausedStates = new()
    {
        SubscriptionStates.Paused, SubscriptionStates.OnHold
    };

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

    public Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken ct = default) =>
        _billingClient.ListPlansAsync(ct);

    public async Task<CustomerSubscription> SubscribeAsync(string customerReference, string email, string planHandle,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        var (firstName, lastName) = DeriveNameParts(email);
        var customer = await _billingClient.EnsureCustomerAsync(customerReference, email, firstName, lastName, ct);

        // Duplicate-subscribe guard: never create a second enrollment for a customer that already has one.
        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
        var alreadySubscribed = existingSubscriptions.FirstOrDefault(s => !TerminalStates.Contains(s.State));
        if (alreadySubscribed != null)
        {
            return alreadySubscribed;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, ct);

        await PublishBestEffortAsync(
            new SubscriptionActivated(customerReference, subscription.Id, subscription.PlanHandle), ct);

        return subscription;
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(string customerReference,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(customerReference, ct);
        if (customer == null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
    }

    public async Task<CustomerSubscription> GetSubscriptionAsync(string? ownerReference, int subscriptionId,
        CancellationToken ct = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, ct);
        AuthorizeOwnership(ownerReference, subscription);
        return subscription;
    }

    public async Task<UsageRecordResult> RecordUsageAsync(string? ownerReference, int subscriptionId,
        double quantity, string? memo, CancellationToken ct = default)
    {
        if (quantity <= 0)
        {
            throw new InvalidSubscriptionRequestException("Usage quantity must be a positive number.");
        }

        var subscription = await GetSubscriptionAsync(ownerReference, subscriptionId, ct);
        if (subscription.State != SubscriptionStates.Active && subscription.State != SubscriptionStates.Trialing)
        {
            throw new SubscriptionConflictException(
                $"Subscription {subscriptionId} has no active billing period (current state: {subscription.State}); usage cannot be recorded.");
        }

        await _billingClient.EnsureMeteredComponentIsValidAsync(ct);

        var usage = await _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, ct);
        var balance = await _billingClient.TryGetMeteredComponentBalanceAsync(subscriptionId, ct);

        return new UsageRecordResult(usage.UsageId, usage.Quantity, usage.RecordedAt, balance);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string? ownerReference, int subscriptionId,
        string targetPlanHandle, CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetSubscriptionAsync(ownerReference, subscriptionId, ct);
        EnsurePlanChangeIsPossible(subscription, targetPlanHandle);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, ct);
    }

    public async Task<CustomerSubscription> CommitPlanChangeAsync(string? ownerReference, int subscriptionId,
        string targetPlanHandle, PlanChangeTiming timing, long? expectedProratedAdjustmentInCents,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetSubscriptionAsync(ownerReference, subscriptionId, ct);
        EnsurePlanChangeIsPossible(subscription, targetPlanHandle);

        CustomerSubscription updated;
        if (timing == PlanChangeTiming.Now)
        {
            var freshPreview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, ct);
            if (expectedProratedAdjustmentInCents.HasValue &&
                freshPreview.ProratedAdjustmentInCents != expectedProratedAdjustmentInCents.Value)
            {
                throw new SubscriptionConflictException(
                    "The previewed proration amount is no longer current; request a fresh preview before committing.");
            }

            updated = await _billingClient.ApplyPlanChangeNowAsync(subscriptionId, targetPlanHandle, ct);
        }
        else
        {
            updated = await _billingClient.SchedulePlanChangeAtRenewalAsync(subscriptionId, targetPlanHandle, ct);
        }

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(subscription.CustomerReference, subscriptionId, subscription.PlanHandle,
                targetPlanHandle, timing == PlanChangeTiming.Now), ct);

        return updated;
    }

    public async Task<CustomerSubscription> PauseAsync(string? ownerReference, int subscriptionId,
        CancellationToken ct = default)
    {
        var subscription = await GetSubscriptionAsync(ownerReference, subscriptionId, ct);

        if (PausedStates.Contains(subscription.State))
        {
            throw new SubscriptionConflictException($"Subscription {subscriptionId} is already paused.");
        }
        if (TerminalStates.Contains(subscription.State))
        {
            throw new SubscriptionConflictException(
                $"Subscription {subscriptionId} cannot be paused from its current state ({subscription.State}).");
        }

        var updated = await _billingClient.PauseSubscriptionAsync(subscriptionId, ct);
        await PublishStateChangedAsync(subscription, updated, ct);
        return updated;
    }

    public async Task<CustomerSubscription> ResumeAsync(string? ownerReference, int subscriptionId,
        CancellationToken ct = default)
    {
        var subscription = await GetSubscriptionAsync(ownerReference, subscriptionId, ct);

        if (!PausedStates.Contains(subscription.State))
        {
            throw new SubscriptionConflictException(
                $"Subscription {subscriptionId} is not paused (current state: {subscription.State}); nothing to resume.");
        }

        var updated = await _billingClient.ResumeSubscriptionAsync(subscriptionId, ct);
        await PublishStateChangedAsync(subscription, updated, ct);
        return updated;
    }

    public async Task<CustomerSubscription> CancelAsync(string? ownerReference, int subscriptionId, string? reason,
        bool endOfPeriod, CancellationToken ct = default)
    {
        var subscription = await GetSubscriptionAsync(ownerReference, subscriptionId, ct);

        if (subscription.State == SubscriptionStates.Canceled)
        {
            throw new SubscriptionConflictException($"Subscription {subscriptionId} is already cancelled.");
        }

        var updated = await _billingClient.CancelSubscriptionAsync(subscriptionId, reason, endOfPeriod, ct);
        await PublishStateChangedAsync(subscription, updated, ct);
        return updated;
    }

    public async Task<CustomerSubscription> ReactivateAsync(string? ownerReference, int subscriptionId,
        CancellationToken ct = default)
    {
        var subscription = await GetSubscriptionAsync(ownerReference, subscriptionId, ct);

        if (!subscription.State.Equals(SubscriptionStates.Canceled, StringComparison.Ordinal) &&
            !subscription.State.Equals(SubscriptionStates.Expired, StringComparison.Ordinal))
        {
            throw new SubscriptionConflictException(
                $"Subscription {subscriptionId} is not cancelled or expired (current state: {subscription.State}); nothing to reactivate.");
        }

        var updated = await _billingClient.ReactivateSubscriptionAsync(subscriptionId, ct);
        await PublishStateChangedAsync(subscription, updated, ct);
        return updated;
    }

    private static void EnsurePlanChangeIsPossible(CustomerSubscription subscription, string targetPlanHandle)
    {
        if (subscription.PlanHandle == targetPlanHandle)
        {
            throw new SubscriptionConflictException("The target plan is the same as the current plan.");
        }

        if (subscription.State != SubscriptionStates.Active && subscription.State != SubscriptionStates.Trialing)
        {
            throw new SubscriptionConflictException(
                $"Subscription {subscription.Id} is not in a state that allows a plan change (current state: {subscription.State}).");
        }
    }

    private static void AuthorizeOwnership(string? ownerReference, CustomerSubscription subscription)
    {
        if (ownerReference != null &&
            !subscription.CustomerReference.Equals(ownerReference, StringComparison.Ordinal))
        {
            // Don't reveal that a subscription id belongs to someone else — report it as not found.
            throw new SubscriptionNotFoundException(subscription.Id);
        }
    }

    private static (string firstName, string lastName) DeriveNameParts(string email)
    {
        var atIndex = email.IndexOf('@');
        var firstName = atIndex > 0 ? email[..atIndex] : email;
        return (firstName, "eShopOnWeb Customer");
    }

    private Task PublishStateChangedAsync(CustomerSubscription before, CustomerSubscription after,
        CancellationToken ct) =>
        PublishBestEffortAsync(
            new SubscriptionStateChanged(before.CustomerReference, before.Id, before.State, after.State), ct);

    private async Task PublishBestEffortAsync(INotification notification, CancellationToken ct)
    {
        try
        {
            await _publisher.Publish(notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to publish {0} notification: {1}", notification.GetType().Name, ex.Message);
        }
    }
}
