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
/// Orchestrates the subscription use cases: validates the request, drives the billing client, and
/// announces the result in-process through MediatR.
/// </summary>
/// <remarks>
/// Notification publishing is best-effort by design (there is no broker and no outbox): once the
/// provider has accepted a change, a failing handler is logged and the change still stands.
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly ISubscriptionCatalogSettings _catalogSettings;

    public SubscriptionService(IBillingClient billingClient,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger,
        ISubscriptionCatalogSettings catalogSettings)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
        _catalogSettings = catalogSettings;
    }

    public Task<IReadOnlyCollection<BillingPlan>> GetAvailablePlansAsync(
        CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userReference, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"Plan '{planHandle}' does not exist at the billing provider. " +
                "Re-seed the sandbox or correct the configured plan handles before subscribing.");

        var customer = await _billingClient.EnsureCustomerAsync(
            userReference,
            userReference,
            DeriveFirstName(userReference),
            DeriveLastName(userReference),
            cancellationToken);

        // A repeated subscribe (double-click, retried request) must never create a second enrollment.
        var existing = await _billingClient.ListSubscriptionsAsync(customer, cancellationToken);
        var live = existing.FirstOrDefault(s => s.IsActive);
        if (live is not null)
        {
            _logger.LogInformation(
                $"Subscribe for {userReference} short-circuited: subscription {live.ProviderSubscriptionId} is already {live.State}.");
            return live;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer, plan.Handle, cancellationToken);

        await PublishAsync(new SubscriptionActivated(subscription), cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyCollection<Subscription>> GetSubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _billingClient.ListSubscriptionsAsync(customer, cancellationToken);
    }

    public async Task<Subscription?> GetCurrentSubscriptionAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await GetSubscriptionsAsync(userReference, cancellationToken);

        // Prefer a live subscription, then one that is merely paused, then the most recent of any.
        return subscriptions.FirstOrDefault(s => s.IsActive)
            ?? subscriptions.FirstOrDefault(s => s.State == SubscriptionState.Paused)
            ?? subscriptions.OrderByDescending(s => s.ProviderSubscriptionId).FirstOrDefault();
    }

    public async Task<BillingComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default)
    {
        var handle = _catalogSettings.MeteredComponentHandle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingConfigurationException("'Maxio:MeteredComponentHandle' is not configured.");
        }

        var component = await _billingClient.FindComponentByHandleAsync(handle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"Metered component '{handle}' does not exist at the billing provider. Re-seed the sandbox.");

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' is {component.Kind}, not metered, so usage cannot be recorded against it. " +
                "A component's kind cannot be changed in place — archive it and recreate it as metered.");
        }

        var expectedFamily = _catalogSettings.ProductFamilyHandle;
        if (!string.IsNullOrWhiteSpace(expectedFamily) &&
            !string.IsNullOrWhiteSpace(component.ProductFamilyHandle) &&
            !string.Equals(component.ProductFamilyHandle, expectedFamily, StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingConfigurationException(
                $"Component '{handle}' belongs to product family '{component.ProductFamilyHandle}', " +
                $"not the configured '{expectedFamily}', so it is not available to these plans.");
        }

        return component;
    }

    public async Task<UsageReport> RecordUsageAsync(string userReference, decimal quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscription = await GetCurrentSubscriptionAsync(userReference, cancellationToken)
            ?? throw new SubscriptionNotFoundException(userReference);

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionTransitionException(subscription.ProviderSubscriptionId,
                subscription.State, SubscriptionLifecycleAction.Resume, LegalActions(subscription));
        }

        return await RecordUsageCoreAsync(subscription.ProviderSubscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageForSubscriptionAsync(int providerSubscriptionId, decimal quantity,
        string? memo, CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(providerSubscriptionId, cancellationToken);
        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionTransitionException(subscription.ProviderSubscriptionId,
                subscription.State, SubscriptionLifecycleAction.Resume, LegalActions(subscription));
        }

        return await RecordUsageCoreAsync(providerSubscriptionId, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport?> GetUsageSummaryAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetCurrentSubscriptionAsync(userReference, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        var component = await GetMeteredComponentAsync(cancellationToken);
        var units = await ReadPeriodToDateUnitsAsync(subscription.ProviderSubscriptionId, component, cancellationToken);

        var empty = new UsageRecord(0, subscription.ProviderSubscriptionId, component.Id, component.Handle,
            0m, null, null);

        return new UsageReport(empty, units, component.UnitPrice);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetCurrentSubscriptionAsync(userReference, cancellationToken)
            ?? throw new SubscriptionNotFoundException(userReference);

        return await PreviewPlanChangeCoreAsync(subscription, targetPlanHandle, timing, cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(string userReference, string targetPlanHandle,
        PlanChangeTiming timing, string previewFingerprint, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));
        Guard.Against.NullOrEmpty(previewFingerprint, nameof(previewFingerprint));

        var subscription = await GetCurrentSubscriptionAsync(userReference, cancellationToken)
            ?? throw new SubscriptionNotFoundException(userReference);

        var previousPlanHandle = subscription.Plan.Handle;

        // Re-price against the provider and refuse to commit if the customer confirmed other numbers.
        var current = await PreviewPlanChangeCoreAsync(subscription, targetPlanHandle, timing, cancellationToken);
        if (!string.Equals(current.Fingerprint, previewFingerprint, StringComparison.Ordinal))
        {
            throw new StalePlanChangePreviewException(targetPlanHandle);
        }

        var updated = await _billingClient.ChangePlanAsync(subscription, targetPlanHandle, timing, cancellationToken);

        await PublishAsync(new SubscriptionPlanChanged(updated, previousPlanHandle, timing, current),
            cancellationToken);

        return updated;
    }

    public async Task<Subscription> ExecuteLifecycleActionAsync(string userReference,
        SubscriptionLifecycleAction action, CancellationTiming cancellationTiming, string? reason,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscription = await GetCurrentSubscriptionAsync(userReference, cancellationToken)
            ?? throw new SubscriptionNotFoundException(userReference);

        return await ExecuteLifecycleActionCoreAsync(subscription, action, cancellationTiming, reason,
            cancellationToken);
    }

    public async Task<Subscription> ExecuteLifecycleActionForSubscriptionAsync(int providerSubscriptionId,
        SubscriptionLifecycleAction action, CancellationTiming cancellationTiming, string? reason,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(providerSubscriptionId, cancellationToken);
        return await ExecuteLifecycleActionCoreAsync(subscription, action, cancellationTiming, reason,
            cancellationToken);
    }

    private async Task<UsageReport> RecordUsageCoreAsync(int providerSubscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity),
                "Reported usage must be a positive quantity.");
        }

        var component = await GetMeteredComponentAsync(cancellationToken);

        var record = await _billingClient.RecordUsageAsync(providerSubscriptionId, component, quantity, memo,
            cancellationToken);

        var units = await ReadPeriodToDateUnitsAsync(providerSubscriptionId, component, cancellationToken);

        return new UsageReport(record, units, component.UnitPrice);
    }

    /// <summary>
    /// Reads the running period-to-date balance. The usage has already been accepted at this point,
    /// so a failure here is reported as "total unavailable" rather than failing the whole operation.
    /// </summary>
    private async Task<int?> ReadPeriodToDateUnitsAsync(int providerSubscriptionId, BillingComponent component,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient.GetPeriodToDateUnitsAsync(providerSubscriptionId, component,
                cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                $"Usage was recorded on subscription {providerSubscriptionId} but the period-to-date total could not be read: {ex.Message}");
            return null;
        }
    }

    private async Task<PlanChangePreview> PreviewPlanChangeCoreAsync(Subscription subscription,
        string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken)
    {
        if (string.Equals(subscription.Plan.Handle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Subscription {subscription.ProviderSubscriptionId} is already on plan '{targetPlanHandle}'.");
        }

        if (!subscription.CanChangePlan)
        {
            throw new InvalidSubscriptionTransitionException(subscription.ProviderSubscriptionId,
                subscription.State, SubscriptionLifecycleAction.Reactivate, LegalActions(subscription));
        }

        _ = await _billingClient.FindPlanByHandleAsync(targetPlanHandle, cancellationToken)
            ?? throw new BillingConfigurationException(
                $"Plan '{targetPlanHandle}' does not exist at the billing provider. Re-seed the sandbox.");

        return await _billingClient.PreviewPlanChangeAsync(subscription, targetPlanHandle, timing,
            cancellationToken);
    }

    private async Task<Subscription> ExecuteLifecycleActionCoreAsync(Subscription subscription,
        SubscriptionLifecycleAction action, CancellationTiming cancellationTiming, string? reason,
        CancellationToken cancellationToken)
    {
        EnsureTransitionIsLegal(subscription, action);

        var previousState = subscription.State;
        var id = subscription.ProviderSubscriptionId;

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause => await _billingClient.PauseSubscriptionAsync(id, cancellationToken),
            SubscriptionLifecycleAction.Resume => await _billingClient.ResumeSubscriptionAsync(id, cancellationToken),
            SubscriptionLifecycleAction.Cancel => await _billingClient.CancelSubscriptionAsync(id, cancellationTiming,
                reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate => await _billingClient.ReactivateSubscriptionAsync(id,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported lifecycle action.")
        };

        await PublishAsync(new SubscriptionStateChanged(updated, previousState, action), cancellationToken);

        return updated;
    }

    private static void EnsureTransitionIsLegal(Subscription subscription, SubscriptionLifecycleAction action)
    {
        var legal = action switch
        {
            SubscriptionLifecycleAction.Pause => subscription.CanPause,
            SubscriptionLifecycleAction.Resume => subscription.CanResume,
            SubscriptionLifecycleAction.Cancel => subscription.CanCancel,
            SubscriptionLifecycleAction.Reactivate => subscription.CanReactivate,
            _ => false
        };

        if (!legal)
        {
            throw new InvalidSubscriptionTransitionException(subscription.ProviderSubscriptionId,
                subscription.State, action, LegalActions(subscription));
        }
    }

    private static IEnumerable<SubscriptionLifecycleAction> LegalActions(Subscription subscription)
    {
        if (subscription.CanPause) yield return SubscriptionLifecycleAction.Pause;
        if (subscription.CanResume) yield return SubscriptionLifecycleAction.Resume;
        if (subscription.CanCancel) yield return SubscriptionLifecycleAction.Cancel;
        if (subscription.CanReactivate) yield return SubscriptionLifecycleAction.Reactivate;
    }

    /// <summary>
    /// Publishes a lifecycle notification without letting an in-process handler failure undo work the
    /// provider has already committed (§2.5 — best-effort, in-process only).
    /// </summary>
    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                $"A handler for {notification.GetType().Name} failed; the billing change still stands. {ex.Message}");
        }
    }

    /// <summary>
    /// eShopOnWeb identities carry no given/family name, so a display name is derived from the
    /// username the provider record is keyed on.
    /// </summary>
    private static string DeriveFirstName(string userReference)
    {
        var localPart = LocalPart(userReference);
        var separator = localPart.IndexOfAny(new[] { '.', '_', '-' });
        return separator > 0 ? localPart[..separator] : localPart;
    }

    private static string DeriveLastName(string userReference)
    {
        var localPart = LocalPart(userReference);
        var separator = localPart.IndexOfAny(new[] { '.', '_', '-' });
        return separator > 0 && separator < localPart.Length - 1
            ? localPart[(separator + 1)..]
            : "eShopOnWeb";
    }

    private static string LocalPart(string userReference)
    {
        var at = userReference.IndexOf('@');
        var localPart = at > 0 ? userReference[..at] : userReference;
        return string.IsNullOrWhiteSpace(localPart) ? userReference : localPart;
    }
}
