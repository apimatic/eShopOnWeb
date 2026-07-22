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
/// Orchestrates the subscription use cases. Mirrors <see cref="OrderService"/>: it validates,
/// calls the provider seam, and announces the outcome in-process. It holds no provider knowledge
/// of its own — everything outbound goes through <see cref="IBillingClient"/>.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly ISubscriptionCatalogSettings _catalogSettings;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IBillingClient billingClient,
        ISubscriptionCatalogSettings catalogSettings,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _catalogSettings = catalogSettings;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        return _billingClient.ListPlansAsync(cancellationToken);
    }

    public async Task<Subscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        // Never enroll against a guessed plan: an unresolvable handle means the configuration and
        // the provider catalog have drifted apart, which is a seeding problem (UC0), not a
        // customer-facing one.
        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan handle '{planHandle}' does not resolve to a plan at the billing provider. " +
                "Re-seed the provider catalog or correct the configured handles.");
        }

        if (plan.IsArchived)
        {
            throw new BillingConfigurationException(
                $"Plan handle '{planHandle}' resolves to an archived plan and cannot be subscribed to.");
        }

        var customer = await _billingClient.EnsureCustomerAsync(BuildCustomerDetails(userName), cancellationToken);

        // Duplicate subscribe (double-click, repeated call): reuse the enrollment that already
        // exists rather than creating a second one.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(s => s.IsActive);
        if (alreadyActive is not null)
        {
            _logger.LogInformation(
                "Subscribe request for {UserName} on plan {PlanHandle} reused existing active subscription {SubscriptionId}.",
                userName, planHandle, alreadyActive.Id);
            return alreadyActive;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(userName, subscription), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        // A user who has never subscribed has no provider-side customer, which is not an error.
        var customer = await _billingClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<Subscription?> FindActiveSubscriptionAsync(string userName, CancellationToken cancellationToken = default)
    {
        var subscriptions = await ListSubscriptionsAsync(userName, cancellationToken);
        return subscriptions.FirstOrDefault(s => s.IsActive);
    }

    public async Task<UsageReceipt> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        // Reject invalid input before anything is sent to the provider.
        if (quantity <= 0)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage quantity must be greater than zero, but was {quantity}.");
        }

        var component = await GetVerifiedMeteredComponentAsync(cancellationToken);

        var subscription = await _billingClient.FindSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingProviderNotFoundException(
                nameof(RecordUsageAsync), $"No subscription with id {subscriptionId} exists at the billing provider.");

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage cannot be recorded against subscription {subscriptionId} because it is {subscription.State}.");
        }

        var recorded = await _billingClient.RecordUsageAsync(subscriptionId, component.Id, quantity, memo, cancellationToken);

        // The usage stands even if reading the running total back fails; report success with the
        // total marked unavailable rather than failing the whole operation.
        int? periodToDate = null;
        try
        {
            periodToDate = await _billingClient.GetPeriodToDateUnitsAsync(subscriptionId, component.Id, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                "Usage was recorded on subscription {SubscriptionId} but the period-to-date total could not be read: {Message}",
                subscriptionId, ex.Message);
        }

        return new UsageReceipt { Recorded = recorded, PeriodToDateUnits = periodToDate };
    }

    public async Task<UsageReceipt?> RecordUsageForUserAsync(string userName, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        var subscription = await FindActiveSubscriptionAsync(userName, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        return await RecordUsageAsync(subscription.Id, quantity, memo, cancellationToken);
    }

    public async Task<int?> GetPeriodToDateUnitsAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var component = await GetVerifiedMeteredComponentAsync(cancellationToken);
        return await _billingClient.GetPeriodToDateUnitsAsync(subscriptionId, component.Id, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await _billingClient.FindSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingProviderNotFoundException(
                nameof(PreviewPlanChangeAsync), $"No subscription with id {subscriptionId} exists at the billing provider.");

        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} is already on plan '{targetPlanHandle}'.");
        }

        if (!subscription.CanChangePlan)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} cannot change plan while it is {subscription.State}. " +
                "Reactivate it first.");
        }

        var targetPlan = await _billingClient.FindPlanByHandleAsync(targetPlanHandle, cancellationToken);
        if (targetPlan is null || targetPlan.IsArchived)
        {
            throw new BillingConfigurationException(
                $"Target plan handle '{targetPlanHandle}' does not resolve to an active plan at the billing provider. " +
                "Re-seed the provider catalog or correct the configured handles.");
        }

        return await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string previewToken,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(previewToken, nameof(previewToken));

        // Re-quote and compare: the customer must never be charged an amount other than the one
        // they were shown. This also re-runs every precondition check.
        var currentQuote = await PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);
        if (!string.Equals(currentQuote.Token, previewToken, StringComparison.Ordinal))
        {
            throw new InvalidSubscriptionOperationException(
                "The plan change quote has changed since it was previewed. Review the new quote and confirm again.");
        }

        var previousPlanHandle = currentQuote.CurrentPlanHandle;
        var subscription = await _billingClient.ChangePlanAsync(subscriptionId, targetPlanHandle, timing, cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(subscription, previousPlanHandle, targetPlanHandle, timing, currentQuote.PaymentDue),
            cancellationToken);

        return subscription;
    }

    public async Task<Subscription> ApplyLifecycleActionAsync(
        int subscriptionId,
        SubscriptionLifecycleAction action,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.FindSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new BillingProviderNotFoundException(
                nameof(ApplyLifecycleActionAsync), $"No subscription with id {subscriptionId} exists at the billing provider.");

        // Reject illegal transitions locally so no provider call is made at all.
        if (!subscription.IsActionLegal(action))
        {
            var legal = subscription.LegalActions.Count == 0
                ? "none"
                : string.Join(", ", subscription.LegalActions);

            throw new InvalidSubscriptionOperationException(
                $"'{action}' is not a legal transition for subscription {subscriptionId} in state {subscription.State}. " +
                $"Legal transitions: {legal}.");
        }

        var previousState = subscription.State;

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause =>
                await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Resume =>
                await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Cancel =>
                await _billingClient.CancelSubscriptionAsync(subscriptionId, reason, cancellationToken),
            SubscriptionLifecycleAction.CancelAtEndOfPeriod =>
                await _billingClient.CancelSubscriptionAtEndOfPeriodAsync(subscriptionId, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate =>
                await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken),
            _ => throw new InvalidSubscriptionOperationException($"Unsupported lifecycle action '{action}'.")
        };

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(updated, previousState, updated.State, action, reason),
            cancellationToken);

        return updated;
    }

    public async Task<MeteredComponent> GetVerifiedMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        var handle = _catalogSettings.MeteredComponentHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException(
                "No metered component handle is configured. Set Maxio:MeteredComponentHandle before recording usage.");
        }

        var component = await _billingClient.FindComponentByHandleAsync(handle, cancellationToken);
        if (component is null)
        {
            throw new BillingConfigurationException(
                $"Metered component handle '{handle}' does not resolve to a component at the billing provider. " +
                "Seed it on the product family (UC0) before recording usage.");
        }

        // A component's kind cannot be converted in place: a non-metered component must be
        // archived and recreated, so refuse rather than producing a confusing provider error.
        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is of kind '{component.Kind ?? "unknown"}', not metered. " +
                "Archive it and recreate it as a metered component before recording usage.");
        }

        if (component.IsArchived)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is archived at the billing provider and cannot accept usage.");
        }

        var expectedFamily = _catalogSettings.ProductFamilyHandle;
        if (!string.IsNullOrWhiteSpace(expectedFamily)
            && !string.IsNullOrWhiteSpace(component.ProductFamilyHandle)
            && !string.Equals(component.ProductFamilyHandle, expectedFamily, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' belongs to product family '{component.ProductFamilyHandle}', " +
                $"not the configured family '{expectedFamily}'. Recreate it on the correct family.");
        }

        return component;
    }

    /// <summary>
    /// Builds the provider-side customer details for an eShopOnWeb user. eShopOnWeb identities are
    /// email addresses, and the same value doubles as the idempotency reference (§4.4).
    /// </summary>
    private static BillingCustomerDetails BuildCustomerDetails(string userName)
    {
        var localPart = userName.Contains('@', StringComparison.Ordinal)
            ? userName[..userName.IndexOf('@', StringComparison.Ordinal)]
            : userName;

        return new BillingCustomerDetails
        {
            Reference = userName,
            Email = userName,
            FirstName = string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart,
            LastName = "Customer"
        };
    }

    /// <summary>
    /// Publishes an in-process notification without ever letting a handler failure undo work the
    /// provider has already committed. There is no durable outbox, so delivery is best-effort
    /// by design (§2.5).
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
                "In-process publication of {NotificationType} failed after the billing operation had already succeeded: {Message}",
                notification.GetType().Name, ex.Message);
        }
    }
}
