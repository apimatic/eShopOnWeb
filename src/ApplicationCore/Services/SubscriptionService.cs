using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the subscription use cases: validate locally, call the billing client, then announce the
/// change through the in-process mediator. Notification delivery is best-effort — a handler failure never
/// undoes work the provider has already committed.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private static readonly BillingSubscriptionState[] _statesAllowingUsage =
    {
        BillingSubscriptionState.Active, BillingSubscriptionState.Trialing
    };

    private static readonly BillingSubscriptionState[] _statesAllowingPlanChange =
    {
        BillingSubscriptionState.Active, BillingSubscriptionState.Trialing
    };

    private static readonly BillingSubscriptionState[] _statesAllowingReactivate =
    {
        BillingSubscriptionState.Canceled, BillingSubscriptionState.Expired,
        BillingSubscriptionState.Unpaid, BillingSubscriptionState.TrialEnded
    };

    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly SubscriptionSettings _settings;

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

    public Task<IReadOnlyList<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
    {
        return _billingClient.ListPlansAsync(cancellationToken);
    }

    public async Task<BillingSubscription> SubscribeAsync(string userReference, string? planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var handle = ResolvePlanHandle(planHandle);

        var plan = await _billingClient.FindPlanByHandleAsync(handle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan handle '{handle}' does not resolve at the billing provider. Re-seed the product family before subscribing.");
        }

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken)
            ?? await _billingClient.CreateCustomerAsync(BuildCustomer(userReference), cancellationToken);

        // Duplicate subscribe (double-click, retried call): return the existing enrollment rather than
        // creating a second one.
        var existing = await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var alreadyActive = existing.FirstOrDefault(s => _statesAllowingUsage.Contains(s.State));
        if (alreadyActive is not null)
        {
            return alreadyActive;
        }

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, handle, cancellationToken);

        await PublishAsync(new SubscriptionActivated(userReference, subscription.Id, subscription.PlanHandle),
            cancellationToken);

        return subscription;
    }

    public async Task<IReadOnlyList<BillingSubscription>> GetMySubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<BillingSubscription>();
        }

        return await _billingClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageAsync(string userReference, int subscriptionId, decimal quantity,
        string? memo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        if (quantity <= 0)
        {
            throw new InvalidSubscriptionOperationException(
                "Usage quantity must be greater than zero.");
        }

        EnsureSubscriptionIdIsValid(subscriptionId);

        var componentHandle = _settings.MeteredComponentHandle;
        if (string.IsNullOrWhiteSpace(componentHandle))
        {
            throw new BillingConfigurationException(
                "No metered component handle is configured, so usage cannot be recorded.");
        }

        var subscription = await GetSubscriptionOrThrowAsync(subscriptionId, cancellationToken);

        if (!_statesAllowingUsage.Contains(subscription.State))
        {
            throw new InvalidSubscriptionOperationException(
                $"Usage cannot be recorded because subscription {subscriptionId} is {subscription.State}, not active.");
        }

        // Refuse to meter against a component that is not actually metered (UC2 precondition).
        var component = await _billingClient.FindMeteredComponentAsync(componentHandle, cancellationToken);
        if (component is null)
        {
            throw new BillingConfigurationException(
                $"Metered component '{componentHandle}' does not resolve at the billing provider.");
        }

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{componentHandle}' is of kind '{component.Kind}', not metered, so usage cannot be recorded against it.");
        }

        var receipt = await _billingClient.RecordUsageAsync(subscriptionId, componentHandle, quantity, memo,
            cancellationToken);

        // The usage already stands; a failed read-back must not fail the whole operation.
        decimal? periodToDate;
        try
        {
            periodToDate = await _billingClient.GetComponentUnitBalanceAsync(subscriptionId, component.Id,
                cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                $"Usage was recorded on subscription {subscriptionId} but the period-to-date total could not be read: {ex.Message}");
            periodToDate = null;
        }

        return new UsageReport(receipt, periodToDate);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference, int subscriptionId,
        string targetPlanHandle, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        // Local validation runs before anything is sent upstream.
        EnsureSubscriptionIdIsValid(subscriptionId);
        EnsurePlanHandleIsValid(targetPlanHandle);

        var subscription = await GetSubscriptionOrThrowAsync(subscriptionId, cancellationToken);

        EnsurePlanChangeIsLegal(subscription, targetPlanHandle);
        await EnsurePlanResolvesAsync(targetPlanHandle, cancellationToken);

        var preview = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken);
        preview.CurrentPlanHandle = subscription.PlanHandle;
        return preview;
    }

    public async Task<BillingSubscription> ChangePlanAsync(string userReference, int subscriptionId,
        string targetPlanHandle, PlanChangeTiming timing, decimal? acknowledgedProratedAdjustment,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        // Local validation runs before anything is sent upstream.
        EnsureSubscriptionIdIsValid(subscriptionId);
        EnsurePlanHandleIsValid(targetPlanHandle);

        var subscription = await GetSubscriptionOrThrowAsync(subscriptionId, cancellationToken);

        EnsurePlanChangeIsLegal(subscription, targetPlanHandle);
        await EnsurePlanResolvesAsync(targetPlanHandle, cancellationToken);

        // Never apply an amount other than the one the customer was shown.
        if (acknowledgedProratedAdjustment.HasValue && timing == PlanChangeTiming.Immediate)
        {
            var fresh = await _billingClient.PreviewPlanChangeAsync(subscriptionId, targetPlanHandle, cancellationToken);
            if (fresh.ProratedAdjustment != acknowledgedProratedAdjustment.Value)
            {
                throw new InvalidSubscriptionOperationException(
                    "The previewed proration is no longer current. Request a fresh preview before confirming.");
            }
        }

        var previousPlanHandle = subscription.PlanHandle;

        var updated = timing == PlanChangeTiming.Immediate
            ? await _billingClient.ChangePlanNowAsync(subscriptionId, targetPlanHandle, cancellationToken)
            : await _billingClient.ChangePlanAtRenewalAsync(subscriptionId, targetPlanHandle, cancellationToken);

        await PublishAsync(
            new SubscriptionPlanChanged(userReference, subscriptionId, previousPlanHandle, targetPlanHandle, timing),
            cancellationToken);

        return updated;
    }

    public async Task<BillingSubscription> ApplyLifecycleActionAsync(string userReference, int subscriptionId,
        SubscriptionLifecycleAction action, string? reason, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));

        // Local validation runs before anything is sent upstream.
        EnsureSubscriptionIdIsValid(subscriptionId);
        EnsureActionIsKnown(action);

        var subscription = await GetSubscriptionOrThrowAsync(subscriptionId, cancellationToken);
        EnsureTransitionIsLegal(subscription, action);

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

        await PublishAsync(
            new SubscriptionStateChanged(userReference, subscriptionId, previousState, updated.State, action),
            cancellationToken);

        return updated;
    }

    private string ResolvePlanHandle(string? planHandle)
    {
        if (!string.IsNullOrWhiteSpace(planHandle))
        {
            return planHandle;
        }

        if (string.IsNullOrWhiteSpace(_settings.DefaultProductHandle))
        {
            throw new BillingConfigurationException(
                "No plan was requested and no default plan handle is configured.");
        }

        return _settings.DefaultProductHandle;
    }

    private static NewBillingCustomer BuildCustomer(string userReference)
    {
        var localPart = userReference.Split('@')[0];

        return new NewBillingCustomer
        {
            Reference = userReference,
            Email = userReference,
            FirstName = string.IsNullOrWhiteSpace(localPart) ? userReference : localPart,
            LastName = "Customer"
        };
    }

    private async Task<BillingSubscription> GetSubscriptionOrThrowAsync(int subscriptionId,
        CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);

        if (subscription is null)
        {
            throw new SubscriptionNotFoundException(subscriptionId);
        }

        return subscription;
    }

    /// <summary>
    /// Rejects a malformed subscription reference before any provider call is made.
    /// </summary>
    private static void EnsureSubscriptionIdIsValid(int subscriptionId)
    {
        if (subscriptionId <= 0)
        {
            throw new InvalidSubscriptionOperationException(
                $"'{subscriptionId}' is not a valid subscription id.");
        }
    }

    /// <summary>
    /// Rejects a lifecycle action outside the modelled set before any provider call is made, so an
    /// unrecognised action can never be silently treated as the default one.
    /// </summary>
    private static void EnsureActionIsKnown(SubscriptionLifecycleAction action)
    {
        if (!Enum.IsDefined(typeof(SubscriptionLifecycleAction), action))
        {
            throw new InvalidSubscriptionOperationException($"'{action}' is not a supported lifecycle action.");
        }
    }

    /// <summary>
    /// Rejects an empty plan handle before any provider call is made.
    /// </summary>
    private static void EnsurePlanHandleIsValid(string targetPlanHandle)
    {
        if (string.IsNullOrWhiteSpace(targetPlanHandle))
        {
            throw new InvalidSubscriptionOperationException("A target plan handle is required.");
        }
    }

    private async Task EnsurePlanResolvesAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan handle '{planHandle}' does not resolve at the billing provider.");
        }

        if (plan.IsArchived)
        {
            throw new BillingConfigurationException($"Plan '{planHandle}' is archived and cannot be subscribed to.");
        }
    }

    private static void EnsurePlanChangeIsLegal(BillingSubscription subscription, string targetPlanHandle)
    {
        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is already on plan '{targetPlanHandle}'.");
        }

        if (!_statesAllowingPlanChange.Contains(subscription.State))
        {
            throw new InvalidSubscriptionOperationException(
                $"Subscription {subscription.Id} is {subscription.State}; reactivate it before changing plan.");
        }
    }

    private static void EnsureTransitionIsLegal(BillingSubscription subscription, SubscriptionLifecycleAction action)
    {
        var state = subscription.State;

        var legal = action switch
        {
            SubscriptionLifecycleAction.Pause => _statesAllowingUsage.Contains(state),
            SubscriptionLifecycleAction.Resume => state == BillingSubscriptionState.Paused,
            SubscriptionLifecycleAction.Cancel or SubscriptionLifecycleAction.CancelAtEndOfPeriod =>
                state != BillingSubscriptionState.Canceled && state != BillingSubscriptionState.Expired,
            SubscriptionLifecycleAction.Reactivate => _statesAllowingReactivate.Contains(state),
            _ => false
        };

        if (!legal)
        {
            throw new InvalidSubscriptionOperationException(
                $"Cannot {action} subscription {subscription.Id} while it is {state}.");
        }
    }

    /// <summary>
    /// Best-effort in-process publication (§2.5): the provider call has already succeeded, so a failing
    /// handler is logged and swallowed rather than surfaced to the caller.
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
                $"A handler for {notification.GetType().Name} failed; the billing change stands. {ex.Message}");
        }
    }
}
