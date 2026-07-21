using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

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

    public Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default) =>
        _billingClient.ListPlansAsync(ct);

    public async Task<Subscription> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default)
    {
        var customer = await _billingClient.EnsureCustomerAsync(userReference, email, firstName, lastName, ct);

        var existingSubscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
        var existing = existingSubscriptions.FirstOrDefault(s => s.ProductHandle == productHandle && s.BlocksReEnrollment);
        if (existing != null)
        {
            return existing;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, userReference, productHandle, ct);

        await PublishBestEffortAsync(new SubscriptionActivated(userReference, subscription.Id, subscription.ProductHandle), ct);

        return subscription;
    }

    public async Task<IReadOnlyList<Subscription>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        var customer = await _billingClient.FindCustomerAsync(userReference, ct);
        if (customer == null)
        {
            return Array.Empty<Subscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
    }

    public async Task<Subscription?> FindActiveSubscriptionAsync(string userReference, CancellationToken ct = default)
    {
        var customer = await _billingClient.FindCustomerAsync(userReference, ct);
        if (customer == null)
        {
            return null;
        }

        var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, ct);
        return subscriptions.FirstOrDefault(s => s.BlocksReEnrollment);
    }

    public Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken ct = default)
    {
        if (quantity <= 0)
        {
            throw new BillingProviderException("Usage quantity must be a positive number.", BillingErrorKind.Validation);
        }

        return _billingClient.RecordUsageAsync(subscriptionId, quantity, memo, ct);
    }

    public Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken ct = default) =>
        _billingClient.PreviewPlanChangeAsync(subscriptionId, targetProductHandle, ct);

    public async Task<Subscription> CommitPlanChangeAsync(string userReference, int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken ct = default)
    {
        var current = await _billingClient.GetSubscriptionAsync(subscriptionId, ct);

        if (string.Equals(current.ProductHandle, targetProductHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingProviderException($"Subscription {subscriptionId} is already on plan '{targetProductHandle}'.", BillingErrorKind.Validation);
        }

        if (!current.BlocksReEnrollment)
        {
            throw new BillingProviderException($"Subscription {subscriptionId} is not in a state that allows a plan change (current state: {current.Status}).", BillingErrorKind.ProviderRejected);
        }

        var oldProductHandle = current.ProductHandle;
        var updated = applyNow
            ? await _billingClient.CommitPlanChangeNowAsync(subscriptionId, targetProductHandle, ct)
            : await _billingClient.SchedulePlanChangeAtRenewalAsync(subscriptionId, targetProductHandle, ct);

        await PublishBestEffortAsync(new SubscriptionPlanChanged(userReference, subscriptionId, oldProductHandle, targetProductHandle, applyNow), ct);

        return updated;
    }

    public Task<Subscription> PauseAsync(string userReference, int subscriptionId, CancellationToken ct = default) =>
        TransitionAsync(userReference, subscriptionId,
            allowedFrom: new[] { SubscriptionStatus.Active, SubscriptionStatus.Trialing, SubscriptionStatus.PastDue },
            action: "pause",
            transition: () => _billingClient.PauseSubscriptionAsync(subscriptionId, ct),
            ct);

    public Task<Subscription> ResumeAsync(string userReference, int subscriptionId, CancellationToken ct = default) =>
        TransitionAsync(userReference, subscriptionId,
            allowedFrom: new[] { SubscriptionStatus.OnHold, SubscriptionStatus.Paused },
            action: "resume",
            transition: () => _billingClient.ResumeSubscriptionAsync(subscriptionId, ct),
            ct);

    public Task<Subscription> CancelAsync(string userReference, int subscriptionId, bool endOfPeriod, CancellationToken ct = default) =>
        TransitionAsync(userReference, subscriptionId,
            allowedFrom: new[] { SubscriptionStatus.Active, SubscriptionStatus.Trialing, SubscriptionStatus.PastDue, SubscriptionStatus.OnHold, SubscriptionStatus.Suspended },
            action: endOfPeriod ? "cancel at end of period" : "cancel",
            transition: () => _billingClient.CancelSubscriptionAsync(subscriptionId, endOfPeriod, ct),
            ct);

    public Task<Subscription> ReactivateAsync(string userReference, int subscriptionId, CancellationToken ct = default) =>
        TransitionAsync(userReference, subscriptionId,
            allowedFrom: new[] { SubscriptionStatus.Canceled, SubscriptionStatus.Unpaid, SubscriptionStatus.TrialEnded, SubscriptionStatus.Expired },
            action: "reactivate",
            transition: () => _billingClient.ReactivateSubscriptionAsync(subscriptionId, ct),
            ct);

    private async Task<Subscription> TransitionAsync(string userReference, int subscriptionId, SubscriptionStatus[] allowedFrom, string action, Func<Task<Subscription>> transition, CancellationToken ct)
    {
        var current = await _billingClient.GetSubscriptionAsync(subscriptionId, ct);
        if (!allowedFrom.Contains(current.Status))
        {
            throw new BillingProviderException(
                $"Cannot {action} subscription {subscriptionId}: current state is {current.Status}; legal source states are [{string.Join(", ", allowedFrom)}].",
                BillingErrorKind.ProviderRejected);
        }

        var oldStatus = current.Status;
        var updated = await transition();

        await PublishBestEffortAsync(new SubscriptionStateChanged(userReference, subscriptionId, oldStatus, updated.Status), ct);

        return updated;
    }

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
