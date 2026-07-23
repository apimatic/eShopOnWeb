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

public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly SubscriptionSettings _settings;
    private string? _validatedMeteredComponentHandle;

    public SubscriptionService(IBillingClient billingClient,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger,
        SubscriptionSettings settings)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
        _settings = settings;
    }

    public Task<IReadOnlyCollection<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        return _billingClient.ListPlansAsync(cancellationToken);
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userReference, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        // Never enroll against a guessed plan: an unresolvable handle means the seed drifted (UC0).
        var plan = await _billingClient.GetPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan handle '{planHandle}' does not resolve on the billing provider. Re-seed the product family '{_settings.ProductFamilyHandle}' or correct the configuration.");
        }

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken)
            ?? await _billingClient.CreateCustomerAsync(userReference, userReference, cancellationToken);

        // Repeated subscribe calls (double-click, retry) must never create a second enrollment.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(subscription => subscription.IsActive);
        if (alreadyActive is not null)
        {
            _logger.LogInformation("{0} already has active subscription {1}; returning it instead of enrolling again.",
                userReference, alreadyActive.Id);
            return alreadyActive;
        }

        var created = await _billingClient.CreateSubscriptionAsync(userReference, planHandle, cancellationToken);

        await PublishAsync(new SubscriptionActivated(userReference, created.Id, created.PlanHandle, created.PlanPrice),
            cancellationToken);

        return created;
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetMySubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<CustomerSubscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageAsync(string userReference, decimal quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userReference, nameof(userReference));

        var subscriptions = await GetMySubscriptionsAsync(userReference, cancellationToken);
        var active = subscriptions.FirstOrDefault(subscription => subscription.IsActive)
            ?? throw new NoActiveSubscriptionException(userReference);

        return await RecordUsageForSubscriptionAsync(active.Id, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageForSubscriptionAsync(int subscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        // Zero or negative quantities are rejected before anything reaches the provider.
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var componentHandle = await EnsureMeteredComponentAsync(cancellationToken);

        var recorded = await _billingClient.RecordUsageAsync(subscriptionId, componentHandle, quantity, memo,
            cancellationToken);

        // The usage stands even if the read-back fails; report it as unavailable rather than failing.
        decimal? periodToDateTotal;
        try
        {
            periodToDateTotal = await _billingClient.GetUsageBalanceAsync(subscriptionId, componentHandle,
                cancellationToken);
        }
        catch (BillingProviderException exception)
        {
            _logger.LogWarning("Recorded usage {0} on subscription {1} but could not read the period-to-date total: {2}",
                recorded.Id, subscriptionId, exception.Message);
            periodToDateTotal = null;
        }

        return new UsageReport(recorded, periodToDateTotal);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken = default)
    {
        var subscription = await RequirePlanChangeableSubscriptionAsync(subscriptionId, targetPlanHandle,
            cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, cancellationToken);
    }

    public async Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, decimal? previewedPaymentDue, CancellationToken cancellationToken = default)
    {
        var subscription = await RequirePlanChangeableSubscriptionAsync(subscriptionId, targetPlanHandle,
            cancellationToken);

        // Never apply an amount other than the one the customer was shown.
        if (previewedPaymentDue.HasValue && timing == PlanChangeTiming.Immediately)
        {
            var current = await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle,
                cancellationToken);
            if (current.PaymentDue != previewedPaymentDue.Value)
            {
                throw new StalePlanChangePreviewException(previewedPaymentDue.Value, current.PaymentDue);
            }
        }

        var oldPlanHandle = subscription.PlanHandle;
        var changed = await _billingClient.ChangePlanAsync(subscription.Id, targetPlanHandle, timing, cancellationToken);

        await PublishAsync(new SubscriptionPlanChanged(changed.CustomerReference ?? string.Empty, changed.Id,
            oldPlanHandle, targetPlanHandle, timing), cancellationToken);

        return changed;
    }

    public async Task<CustomerSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await RequireSubscriptionAsync(subscriptionId, cancellationToken);
        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, subscription.State, "pause");
        }

        return await ApplyTransitionAsync(subscription, "pause",
            () => _billingClient.PauseSubscriptionAsync(subscriptionId, cancellationToken), cancellationToken);
    }

    public async Task<CustomerSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await RequireSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription.State != SubscriptionState.OnHold)
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, subscription.State, "resume");
        }

        return await ApplyTransitionAsync(subscription, "resume",
            () => _billingClient.ResumeSubscriptionAsync(subscriptionId, cancellationToken), cancellationToken);
    }

    public async Task<CustomerSubscription> CancelAsync(int subscriptionId, CancellationTiming timing, string? reason,
        CancellationToken cancellationToken = default)
    {
        var subscription = await RequireSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription.State == SubscriptionState.Canceled || subscription.State == SubscriptionState.Expired)
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, subscription.State, "cancel");
        }

        return await ApplyTransitionAsync(subscription, "cancel",
            () => _billingClient.CancelSubscriptionAsync(subscriptionId, timing, reason, cancellationToken),
            cancellationToken);
    }

    public async Task<CustomerSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await RequireSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription.State != SubscriptionState.Canceled && subscription.State != SubscriptionState.Expired)
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, subscription.State, "reactivate");
        }

        return await ApplyTransitionAsync(subscription, "reactivate",
            () => _billingClient.ReactivateSubscriptionAsync(subscriptionId, cancellationToken), cancellationToken);
    }

    private async Task<CustomerSubscription> ApplyTransitionAsync(CustomerSubscription subscription, string action,
        Func<Task<CustomerSubscription>> transition, CancellationToken cancellationToken)
    {
        var updated = await transition();

        await PublishAsync(new SubscriptionStateChanged(updated.CustomerReference ?? subscription.CustomerReference ?? string.Empty,
            updated.Id, subscription.State, updated.State, action), cancellationToken);

        return updated;
    }

    private async Task<CustomerSubscription> RequireSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        return await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);
    }

    private async Task<CustomerSubscription> RequirePlanChangeableSubscriptionAsync(int subscriptionId,
        string targetPlanHandle, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await RequireSubscriptionAsync(subscriptionId, cancellationToken);

        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidPlanChangeException(subscriptionId, targetPlanHandle);
        }

        if (!subscription.IsActive)
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, subscription.State, "change the plan of");
        }

        var targetPlan = await _billingClient.GetPlanByHandleAsync(targetPlanHandle, cancellationToken);
        if (targetPlan is null)
        {
            throw new BillingConfigurationException(
                $"Plan handle '{targetPlanHandle}' does not resolve on the billing provider. Re-seed the product family '{_settings.ProductFamilyHandle}' or correct the configuration.");
        }

        return subscription;
    }

    /// <summary>
    /// Refuses to record usage unless the configured handle resolves to a metered component on the
    /// family. Runs before the first usage call and is cached for the lifetime of the service.
    /// </summary>
    private async Task<string> EnsureMeteredComponentAsync(CancellationToken cancellationToken)
    {
        var componentHandle = _settings.MeteredComponentHandle;
        Guard.Against.NullOrEmpty(componentHandle, nameof(componentHandle));

        if (_validatedMeteredComponentHandle == componentHandle)
        {
            return componentHandle;
        }

        var component = await _billingClient.GetComponentByHandleAsync(componentHandle, cancellationToken);
        if (component is null)
        {
            throw new BillingConfigurationException(
                $"Component handle '{componentHandle}' does not resolve on the billing provider. Seed it on product family '{_settings.ProductFamilyHandle}' before recording usage.");
        }

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{componentHandle}' is of kind '{component.Kind}', not '{MeteredComponent.METERED_KIND}'. A component cannot be type-converted in place — archive it and recreate it as metered.");
        }

        _validatedMeteredComponentHandle = componentHandle;
        return componentHandle;
    }

    /// <summary>
    /// Eventing is best-effort and in-process: a failing handler never undoes the billing action
    /// that already succeeded at the provider.
    /// </summary>
    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Publishing {0} failed after the provider call succeeded: {1}",
                notification.GetType().Name, exception.Message);
        }
    }
}
