using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscription use cases: validate, drive the billing provider through
/// <see cref="IBillingClient"/>, then announce the outcome in-process through MediatR.
/// </summary>
/// <remarks>
/// The eShopOnWeb user ↔ provider customer link is stateless: it is re-derived on every call from
/// the user's stable reference, which the provider stores on the customer record. Nothing is
/// persisted in eShopOnWeb's own database.
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IMeteredComponentValidator _meteredComponentValidator;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient,
        IMeteredComponentValidator meteredComponentValidator,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _meteredComponentValidator = meteredComponentValidator;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return _billingClient.ListPlansAsync(cancellationToken);
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userReference,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Never enroll against a guessed plan: a handle that no longer resolves is a seed problem.
        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"The plan handle '{planHandle}' does not resolve at the billing provider. " +
                "Correct the billing seed (UC0) or the configured handles before subscribing.");
        }

        if (plan.IsArchived)
        {
            throw new BillingConfigurationException(
                $"The plan '{planHandle}' is archived at the billing provider and cannot be subscribed to.");
        }

        var customer = await EnsureCustomerAsync(userReference, cancellationToken);

        // A repeated subscribe (double-click, retried call) must return the existing enrollment
        // rather than creating a second one.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var alreadySubscribed = existing.FirstOrDefault(s =>
            s.IsActive && string.Equals(s.PlanHandle, planHandle, StringComparison.OrdinalIgnoreCase));

        if (alreadySubscribed is not null)
        {
            _logger.LogInformation(
                "Subscribe request for {0} on plan {1} matched existing active subscription {2}; returning it.",
                userReference, planHandle, alreadySubscribed.Id);
            return alreadySubscribed;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(userReference, subscription), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<ComponentUsageSummary?> GetUsageAsync(string userReference,
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        await GetOwnedSubscriptionAsync(userReference, subscriptionId, cancellationToken);
        var component = await _meteredComponentValidator.GetValidatedComponentAsync(cancellationToken);

        return await _billingClient.GetComponentUsageAsync(subscriptionId, component, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageAsync(string userReference,
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var subscription = await GetOwnedSubscriptionAsync(userReference, subscriptionId, cancellationToken);
        return await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageForAnyCustomerAsync(int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        return await RecordUsageCoreAsync(subscription, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport?> TryRecordUsageForUserAsync(string userReference,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var subscriptions = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var active = subscriptions.FirstOrDefault(s => s.IsActive);
        if (active is null)
        {
            return null;
        }

        return await RecordUsageCoreAsync(active, quantity, memo, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetOwnedSubscriptionAsync(userReference, subscriptionId, cancellationToken);
        await ValidatePlanChangeAsync(subscription, targetPlanHandle, cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId,
            subscription.PlanHandle ?? string.Empty,
            targetPlanHandle,
            timing,
            cancellationToken);
    }

    public async Task<CustomerSubscription> ChangePlanAsync(string userReference,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        long confirmedPaymentDueInCents,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetOwnedSubscriptionAsync(userReference, subscriptionId, cancellationToken);
        await ValidatePlanChangeAsync(subscription, targetPlanHandle, cancellationToken);

        // Re-price immediately before committing: the customer must never be charged an amount other
        // than the one they were shown.
        var current = await _billingClient.PreviewPlanChangeAsync(subscriptionId,
            subscription.PlanHandle ?? string.Empty,
            targetPlanHandle,
            timing,
            cancellationToken);

        if (current.PaymentDueInCents != confirmedPaymentDueInCents)
        {
            throw new StalePlanChangePreviewException(subscriptionId, confirmedPaymentDueInCents, current.PaymentDueInCents);
        }

        var changed = await _billingClient.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionPlanChanged(userReference,
            subscriptionId,
            subscription.PlanHandle,
            targetPlanHandle,
            timing,
            current,
            changed), cancellationToken);

        return changed;
    }

    public async Task<CustomerSubscription> ApplyLifecycleActionAsync(string userReference,
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var subscription = await GetOwnedSubscriptionAsync(userReference, subscriptionId, cancellationToken);
        return await ApplyLifecycleCoreAsync(userReference, subscription, action, cancellationTiming, reason, cancellationToken);
    }

    public async Task<CustomerSubscription> ApplyLifecycleActionForAnyCustomerAsync(int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);

        return await ApplyLifecycleCoreAsync(subscription.CustomerReference ?? string.Empty,
            subscription, action, cancellationTiming, reason, cancellationToken);
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(string userReference, CancellationToken cancellationToken)
    {
        var existing = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(userReference);
        return await _billingClient.CreateCustomerAsync(userReference, userReference, firstName, lastName, cancellationToken);
    }

    /// <summary>
    /// Derives a display name for the provider-side customer record from the eShopOnWeb user
    /// reference. The provider requires both name parts; eShopOnWeb only guarantees an email.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string userReference)
    {
        var localPart = userReference.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? parts[0] : localPart;
        var lastName = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "eShopOnWeb";

        return (string.IsNullOrWhiteSpace(firstName) ? "eShopOnWeb" : firstName, lastName);
    }

    private async Task<CustomerSubscription> GetOwnedSubscriptionAsync(string userReference,
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        // A subscription that exists but belongs to somebody else is reported exactly like one that
        // does not exist, so subscription ids cannot be probed from the customer-facing surface.
        if (subscription is null ||
            !string.Equals(subscription.CustomerReference, userReference, StringComparison.OrdinalIgnoreCase))
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    private async Task<UsageReport> RecordUsageCoreAsync(CustomerSubscription subscription,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken)
    {
        if (quantity <= 0)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage quantity must be greater than zero; got {quantity}.");
        }

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is '{subscription.ProviderState ?? subscription.Status.ToString()}' " +
                "and cannot accrue usage. Only an active subscription may be billed for usage.");
        }

        // Refuses before any provider call if the configured component is missing or not metered.
        var component = await _meteredComponentValidator.GetValidatedComponentAsync(cancellationToken);

        var recorded = await _billingClient.RecordUsageAsync(subscription.Id,
            component.Handle ?? string.Empty,
            quantity,
            memo,
            cancellationToken);

        // The usage stands even if the running total cannot be read back; never resend the units.
        ComponentUsageSummary? usage = null;
        try
        {
            usage = await _billingClient.GetComponentUsageAsync(subscription.Id, component, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                "Usage {0} was recorded on subscription {1} but the period-to-date total could not be read back: {2}",
                recorded.Id, subscription.Id, ex.ProviderMessage);
        }

        return new UsageReport(subscription.Id, recorded, usage);
    }

    private async Task ValidatePlanChangeAsync(CustomerSubscription subscription,
        string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'; there is nothing to change.");
        }

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is '{subscription.ProviderState ?? subscription.Status.ToString()}' " +
                "and cannot change plan. Reactivate it first.");
        }

        var target = await _billingClient.FindPlanByHandleAsync(targetPlanHandle, cancellationToken);
        if (target is null || target.IsArchived)
        {
            throw new BillingConfigurationException(
                $"The target plan handle '{targetPlanHandle}' does not resolve to an active plan at the billing " +
                "provider. Correct the billing seed (UC0) or the configured handles.");
        }
    }

    private async Task<CustomerSubscription> ApplyLifecycleCoreAsync(string userReference,
        CustomerSubscription subscription,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken)
    {
        EnsureTransitionIsLegal(subscription, action);

        var previousStatus = subscription.Status;

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause =>
                await _billingClient.PauseSubscriptionAsync(subscription.Id, null, cancellationToken),
            SubscriptionLifecycleAction.Resume =>
                await _billingClient.ResumeSubscriptionAsync(subscription.Id, cancellationToken),
            SubscriptionLifecycleAction.Cancel =>
                await _billingClient.CancelSubscriptionAsync(subscription.Id, cancellationTiming, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate =>
                await _billingClient.ReactivateSubscriptionAsync(subscription.Id, cancellationToken),
            _ => throw new InvalidSubscriptionOperationException($"Unsupported lifecycle action '{action}'.")
        };

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(userReference, subscription.Id, action, previousStatus, updated),
            cancellationToken);

        return updated;
    }

    /// <summary>
    /// Rejects a transition that is illegal from the subscription's current state before any provider
    /// call is made, reporting the current state and what is legal from it (UC4 failure scenario).
    /// </summary>
    private static void EnsureTransitionIsLegal(CustomerSubscription subscription, SubscriptionLifecycleAction action)
    {
        var status = subscription.Status;
        var isPaused = status == SubscriptionStatus.OnHold || status == SubscriptionStatus.Paused;
        var isTerminated = status == SubscriptionStatus.Canceled || status == SubscriptionStatus.Expired;

        var legal = action switch
        {
            SubscriptionLifecycleAction.Pause => subscription.IsActive,
            SubscriptionLifecycleAction.Resume => isPaused,
            SubscriptionLifecycleAction.Cancel => !isTerminated,
            SubscriptionLifecycleAction.Reactivate => isTerminated,
            _ => false
        };

        if (legal)
        {
            return;
        }

        var allowed = new List<string>();
        if (subscription.IsActive)
        {
            allowed.Add(nameof(SubscriptionLifecycleAction.Pause));
        }

        if (isPaused)
        {
            allowed.Add(nameof(SubscriptionLifecycleAction.Resume));
        }

        if (!isTerminated)
        {
            allowed.Add(nameof(SubscriptionLifecycleAction.Cancel));
        }
        else
        {
            allowed.Add(nameof(SubscriptionLifecycleAction.Reactivate));
        }

        throw new InvalidSubscriptionOperationException(
            $"Cannot {action} subscription {subscription.Id}: it is " +
            $"'{subscription.ProviderState ?? status.ToString()}'. " +
            $"Legal actions from this state: {(allowed.Count == 0 ? "none" : string.Join(", ", allowed))}.");
    }

    /// <summary>
    /// Publishes a lifecycle notification in-process. Eventing is best-effort (§2.5): a handler that
    /// throws is logged and swallowed, because the provider-side change has already succeeded and
    /// must not be rolled back.
    /// </summary>
    private async Task PublishBestEffortAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "In-process publication of {0} failed after the billing change succeeded; the change stands. Error: {1}",
                notification.GetType().Name, ex.Message);
        }
    }
}
