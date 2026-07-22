using System;
using System.Collections.Generic;
using System.Globalization;
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

public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(IBillingClient billingClient,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = await _billingClient.ListPlansAsync(cancellationToken);

        return plans.Where(plan => !plan.IsArchived)
            .OrderBy(plan => plan.Price)
            .ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan '{planHandle}' does not resolve against the billing provider. Confirm the product catalog has been provisioned with this handle.");
        }

        var customer = await EnsureCustomerAsync(userReference, cancellationToken);

        // Idempotency: a repeated subscribe (double click, retried call) must never create a second
        // enrollment. The provider-side customer reference is what makes this safe to retry.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(subscription => subscription.IsActive);
        if (alreadyActive is not null)
        {
            return alreadyActive;
        }

        var created = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(userReference, created), cancellationToken);

        return created;
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<MeteredComponentDefinition> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        var component = await _billingClient.GetMeteredComponentAsync(cancellationToken);

        if (component is null)
        {
            throw new BillingConfigurationException(
                "The configured metered component does not resolve on the configured product family. Provision it before reporting usage.");
        }

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{component.Handle}' is not of metered kind, so usage cannot be reported against it. Archive it and recreate it as a metered component.");
        }

        return component;
    }

    public async Task<UsageSummary> GetUsageSummaryAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default)
    {
        var component = await GetMeteredComponentAsync(cancellationToken);
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);

        decimal? periodToDate = null;
        int? unitBalance = null;

        try
        {
            unitBalance = await _billingClient.GetComponentUnitBalanceAsync(subscriptionId, component.Id, cancellationToken);
            periodToDate = await _billingClient.SumUsageSinceAsync(subscriptionId, component.Id, subscription.CurrentPeriodStartedAt, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            // The panel is informational: report it as unavailable rather than failing the whole page.
            _logger.LogWarning("Usage totals for subscription {SubscriptionId} could not be read: {Reason}", subscriptionId, ex.Message);
        }

        return new UsageSummary(subscriptionId,
            component.Handle,
            component.UnitName,
            component.UnitPrice,
            periodToDate,
            unitBalance,
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt);
    }

    public async Task<UsageReport> RecordUsageAsync(int subscriptionId,
        decimal quantity,
        string? memo,
        string? ownerReference,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage quantity must be greater than zero, but was {quantity.ToString(CultureInfo.InvariantCulture)}.");
        }

        // Validated before any provider write, so a misconfigured component can never bill a customer.
        var component = await GetMeteredComponentAsync(cancellationToken);

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);
        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscriptionId} is {subscription.Status} and cannot accept usage. Only an active subscription can be billed for usage.");
        }

        var record = await _billingClient.RecordUsageAsync(subscriptionId, component.Id, quantity, memo, cancellationToken);

        // The usage is already recorded. A failure to read the running totals back must not fail the
        // operation, nor cause the caller to re-send and double-bill the same units.
        decimal? periodToDate = null;
        int? unitBalance = null;
        var totalsAvailable = true;

        try
        {
            unitBalance = await _billingClient.GetComponentUnitBalanceAsync(subscriptionId, component.Id, cancellationToken);
            periodToDate = await _billingClient.SumUsageSinceAsync(subscriptionId, component.Id, subscription.CurrentPeriodStartedAt, cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            totalsAvailable = false;
            _logger.LogWarning("Usage {UsageId} was recorded against subscription {SubscriptionId}, but the period-to-date total could not be read back: {Reason}",
                record.Id, subscriptionId, ex.Message);
        }

        return new UsageReport(record, subscriptionId, periodToDate, unitBalance, component.UnitPrice, totalsAvailable);
    }

    public async Task<UsageReport> RecordUsageForUserAsync(string userReference, decimal quantity, string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var subscriptions = await GetSubscriptionsAsync(userReference, cancellationToken);
        var active = subscriptions.FirstOrDefault(subscription => subscription.IsActive);

        if (active is null)
        {
            throw new InvalidSubscriptionOperationException(
                $"'{userReference}' has no active subscription, so no usage can be reported.");
        }

        return await RecordUsageAsync(active.Id, quantity, memo, userReference, cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        string? ownerReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);

        return await BuildPreviewAsync(subscription, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<PlanChangeResult> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        decimal confirmedAmountDue,
        string? ownerReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);

        // Re-price against the provider at commit time. The customer is never charged an amount other
        // than the one they were shown; a moved price rejects the commit instead.
        var preview = await BuildPreviewAsync(subscription, targetPlanHandle, timing, cancellationToken);
        if (preview.AmountDue != confirmedAmountDue)
        {
            throw new InvalidSubscriptionOperationException(
                $"The previewed amount is no longer current: {Money(confirmedAmountDue)} was confirmed but the provider now quotes {Money(preview.AmountDue)}. Request a fresh preview.");
        }

        var updated = timing == PlanChangeTiming.Immediately
            ? await _billingClient.ChangePlanImmediatelyAsync(subscriptionId, targetPlanHandle, cancellationToken)
            : await _billingClient.ChangePlanAtRenewalAsync(subscriptionId, targetPlanHandle, cancellationToken);

        var result = new PlanChangeResult(updated,
            subscription.PlanHandle,
            subscription.PlanName,
            targetPlanHandle,
            preview.TargetPlanName,
            timing,
            preview.AmountDue);

        await PublishBestEffortAsync(new SubscriptionPlanChanged(result), cancellationToken);

        return result;
    }

    public async Task<CustomerSubscription> ApplyLifecycleActionAsync(int subscriptionId,
        SubscriptionLifecycleAction action,
        string? reason,
        string? ownerReference,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetOwnedSubscriptionAsync(subscriptionId, ownerReference, cancellationToken);

        EnsureTransitionIsLegal(subscription, action);

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause => await _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Resume => await _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.CancelImmediately => await _billingClient.CancelSubscriptionImmediatelyAsync(subscriptionId, reason, cancellationToken),
            SubscriptionLifecycleAction.CancelAtPeriodEnd => await _billingClient.CancelSubscriptionAtPeriodEndAsync(subscriptionId, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate => await _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken),
            _ => throw new InvalidSubscriptionOperationException($"Unsupported lifecycle action '{action}'.")
        };

        await PublishBestEffortAsync(new SubscriptionStateChanged(action, subscription.Status, updated), cancellationToken);

        return updated;
    }

    private async Task<BillingCustomer> EnsureCustomerAsync(string userReference, CancellationToken cancellationToken)
    {
        var existing = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveName(userReference);

        return await _billingClient.CreateCustomerAsync(
            new NewBillingCustomer(userReference, userReference, firstName, lastName),
            cancellationToken);
    }

    private async Task<PlanChangePreview> BuildPreviewAsync(CustomerSubscription subscription,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken)
    {
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'.");
        }

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is {subscription.Status} and cannot change plan. Reactivate it first.");
        }

        var targetPlan = await _billingClient.FindPlanByHandleAsync(targetPlanHandle, cancellationToken);
        if (targetPlan is null || targetPlan.IsArchived)
        {
            throw new BillingConfigurationException(
                $"Target plan '{targetPlanHandle}' does not resolve to an available plan. Confirm the product catalog has been provisioned with this handle.");
        }

        if (timing == PlanChangeTiming.AtNextRenewal)
        {
            // Scheduled changes are never prorated: the new price simply applies from the next period.
            return new PlanChangePreview(subscription.Id,
                subscription.PlanHandle,
                subscription.PlanName,
                subscription.PlanPrice,
                targetPlan.Handle,
                targetPlan.Name,
                targetPlan.Price,
                PlanChangeTiming.AtNextRenewal,
                proratedAdjustment: 0m,
                proratedCharge: 0m,
                creditApplied: 0m,
                amountDue: 0m,
                effectiveAt: subscription.CurrentPeriodEndsAt);
        }

        return await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, cancellationToken);
    }

    private async Task<CustomerSubscription> GetOwnedSubscriptionAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (subscription is null)
        {
            throw new InvalidSubscriptionOperationException($"No subscription found with id {subscriptionId}.");
        }

        if (ownerReference is not null &&
            !string.Equals(subscription.CustomerReference, ownerReference, StringComparison.OrdinalIgnoreCase))
        {
            // Deliberately indistinguishable from "not found" so subscription ids cannot be probed.
            throw new InvalidSubscriptionOperationException($"No subscription found with id {subscriptionId}.");
        }

        return subscription;
    }

    private static void EnsureTransitionIsLegal(CustomerSubscription subscription, SubscriptionLifecycleAction action)
    {
        if (IsTransitionLegal(subscription, action))
        {
            return;
        }

        var allowed = Enum.GetValues<SubscriptionLifecycleAction>()
            .Where(candidate => IsTransitionLegal(subscription, candidate))
            .Select(candidate => candidate.ToString())
            .ToList();

        var allowedText = allowed.Count == 0 ? "none" : string.Join(", ", allowed);

        throw new InvalidSubscriptionOperationException(
            $"Subscription {subscription.Id} is {subscription.Status} and cannot be {action}. Legal transitions from this state: {allowedText}.");
    }

    private static bool IsTransitionLegal(CustomerSubscription subscription, SubscriptionLifecycleAction action) => action switch
    {
        SubscriptionLifecycleAction.Pause => subscription.IsActive,
        SubscriptionLifecycleAction.Resume => subscription.IsPaused,
        SubscriptionLifecycleAction.CancelImmediately => subscription.IsActive || subscription.IsPaused
            || subscription.Status is SubscriptionStatus.Unpaid or SubscriptionStatus.Suspended,
        // The provider refuses a delayed cancel while a subscription is past due.
        SubscriptionLifecycleAction.CancelAtPeriodEnd => subscription.IsActive
            && subscription.Status is not SubscriptionStatus.PastDue,
        SubscriptionLifecycleAction.Reactivate => subscription.Status is SubscriptionStatus.Canceled
            or SubscriptionStatus.Expired or SubscriptionStatus.TrialEnded or SubscriptionStatus.Unpaid,
        _ => false
    };

    /// <summary>
    /// Publishes an in-process notification. Delivery is best effort: eShopOnWeb has no durable outbox,
    /// so a failing handler is logged and the completed billing action stands (plan §2.5).
    /// </summary>
    private async Task PublishBestEffortAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("In-process publication of {NotificationType} failed after the billing action succeeded: {Reason}",
                notification.GetType().Name, ex.Message);
        }
    }

    /// <summary>
    /// eShopOnWeb Identity stores only an email/username, but the billing provider requires a first and
    /// last name. Derive something stable and human readable from the local part of the address.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string userReference)
    {
        var localPart = userReference.Split('@')[0];
        var segments = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = segments.Length > 0 ? Capitalize(segments[0]) : "eShopOnWeb";
        var lastName = segments.Length > 1 ? Capitalize(segments[^1]) : "Customer";

        return (firstName, lastName);
    }

    private static string Money(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string Capitalize(string value)
    {
        return value.Length <= 1
            ? value.ToUpperInvariant()
            : char.ToUpperInvariant(value[0]) + value[1..];
    }
}
