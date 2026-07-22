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
/// Orchestrates the subscription use cases: validate, call the billing client, publish the
/// matching in-process notification. Mirrors <see cref="OrderService"/>.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;
    private readonly string _meteredComponentHandle;

    public SubscriptionService(IBillingClient billingClient,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger,
        ISubscriptionSettings settings)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
        _meteredComponentHandle = settings.MeteredComponentHandle;
    }

    public Task<IReadOnlyCollection<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
        => _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(string userName, string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));
        Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));

        // Never enroll against a guessed plan: the configured handle must resolve (UC1 failure scenarios).
        var plan = await _billingClient.FindPlanAsync(planHandle, cancellationToken);
        if (plan is null)
        {
            throw new BillingConfigurationException($"Plan handle '{planHandle}' does not resolve to a product.");
        }

        // A repeated subscribe (double-click, retried call) must return the existing enrollment
        // rather than create a second one.
        var existing = await _billingClient.ListSubscriptionsAsync(userName, cancellationToken);
        var alreadySubscribed = existing.FirstOrDefault(s => s.IsLive && s.PlanHandle == planHandle);
        if (alreadySubscribed is not null)
        {
            _logger.LogInformation("{User} is already subscribed to {Plan}; returning subscription {Id}",
                userName, planHandle, alreadySubscribed.Id);
            return alreadySubscribed;
        }

        // Idempotent on the user reference, so retrying after a failed enrollment is safe.
        var customer = await _billingClient.EnsureCustomerAsync(userName, userName,
            DeriveFirstName(userName), DeriveLastName(userName), cancellationToken);

        var subscription = await _billingClient.CreateSubscriptionAsync(customer.Id, planHandle, cancellationToken);

        await PublishAsync(new SubscriptionActivated(subscription), cancellationToken);

        return subscription;
    }

    public Task<IReadOnlyCollection<Subscription>> GetMySubscriptionsAsync(string userName,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));
        return _billingClient.ListSubscriptionsAsync(userName, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageAsync(string userName, decimal quantity, string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(userName, nameof(userName));

        var subscriptions = await _billingClient.ListSubscriptionsAsync(userName, cancellationToken);
        var live = subscriptions.FirstOrDefault(s => s.IsLive);
        if (live is null)
        {
            throw new NoActiveSubscriptionException(userName);
        }

        return await RecordUsageForSubscriptionAsync(live.Id, quantity, memo, cancellationToken);
    }

    public async Task<UsageReport> RecordUsageForSubscriptionAsync(int subscriptionId, decimal quantity,
        string? memo, CancellationToken cancellationToken = default)
    {
        // Reject invalid input before any provider call (UC2 failure scenarios).
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            throw new BillingConfigurationException($"Subscription {subscriptionId} does not exist.");
        }

        if (!subscription.IsLive)
        {
            throw new NoActiveSubscriptionException(subscription.CustomerReference);
        }

        await EnsureComponentIsMeteredAsync(cancellationToken);

        var record = await _billingClient.RecordUsageAsync(subscriptionId, _meteredComponentHandle,
            quantity, memo, cancellationToken);

        // A failed read-back must not fail the whole operation — the usage already stands.
        decimal? balance;
        try
        {
            balance = await _billingClient.GetUsageBalanceAsync(subscriptionId, _meteredComponentHandle,
                cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning("Recorded usage on subscription {Id} but could not read the running total: {Message}",
                subscriptionId, ex.Message);
            balance = null;
        }

        return new UsageReport(record, balance);
    }

    public Task<decimal?> GetUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default)
        => _billingClient.GetUsageBalanceAsync(subscriptionId, _meteredComponentHandle, cancellationToken);

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await GetChangeablePlanSubscriptionAsync(subscriptionId, targetPlanHandle, cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, timing,
            cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, PlanChangePreview confirmedPreview, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(targetPlanHandle, nameof(targetPlanHandle));
        Guard.Against.Null(confirmedPreview, nameof(confirmedPreview));

        var subscription = await GetChangeablePlanSubscriptionAsync(subscriptionId, targetPlanHandle, cancellationToken);

        // Never apply an amount other than the one the customer was shown (UC3 failure scenarios).
        var currentQuote = await _billingClient.PreviewPlanChangeAsync(subscription.Id, targetPlanHandle, timing,
            cancellationToken);
        if (!confirmedPreview.Matches(currentQuote))
        {
            throw new StalePlanChangePreviewException(subscription.Id);
        }

        var previousPlanHandle = subscription.PlanHandle;
        var changed = await _billingClient.ChangePlanAsync(subscription.Id, targetPlanHandle, timing,
            cancellationToken);

        await PublishAsync(new SubscriptionPlanChanged(changed, previousPlanHandle, timing, currentQuote),
            cancellationToken);

        return changed;
    }

    public async Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId,
        SubscriptionLifecycleAction action, bool cancelAtEndOfPeriod = false, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            throw new BillingConfigurationException($"Subscription {subscriptionId} does not exist.");
        }

        // Illegal transitions are rejected locally — no provider call is made (UC4 failure scenarios).
        if (!IsTransitionLegal(subscription.State, action))
        {
            throw new InvalidSubscriptionTransitionException(subscriptionId, action, subscription.State,
                DescribeLegalActions(subscription.State));
        }

        var previousState = subscription.State;
        var result = action switch
        {
            SubscriptionLifecycleAction.Pause => await _billingClient.PauseAsync(subscriptionId, null, cancellationToken),
            SubscriptionLifecycleAction.Resume => await _billingClient.ResumeAsync(subscriptionId, cancellationToken),
            SubscriptionLifecycleAction.Cancel => await _billingClient.CancelAsync(subscriptionId,
                cancelAtEndOfPeriod, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate => await _billingClient.ReactivateAsync(subscriptionId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported lifecycle action")
        };

        await PublishAsync(new SubscriptionStateChanged(result, previousState, action), cancellationToken);

        return result;
    }

    /// <summary>
    /// Which lifecycle actions the provider will accept from a given state. Kept in one place so
    /// the check and the message shown to the actor can never drift apart.
    /// </summary>
    private static bool IsTransitionLegal(SubscriptionState state, SubscriptionLifecycleAction action) =>
        action switch
        {
            SubscriptionLifecycleAction.Pause => state is SubscriptionState.Active or SubscriptionState.Trialing
                or SubscriptionState.PastDue,
            SubscriptionLifecycleAction.Resume => state is SubscriptionState.OnHold or SubscriptionState.Paused,
            SubscriptionLifecycleAction.Cancel => state is SubscriptionState.Active or SubscriptionState.Trialing
                or SubscriptionState.PastDue or SubscriptionState.OnHold or SubscriptionState.Paused
                or SubscriptionState.Unpaid or SubscriptionState.SoftFailure,
            SubscriptionLifecycleAction.Reactivate => state is SubscriptionState.Canceled or SubscriptionState.Expired
                or SubscriptionState.TrialEnded,
            _ => false
        };

    private static string DescribeLegalActions(SubscriptionState state)
    {
        var legal = Enum.GetValues<SubscriptionLifecycleAction>()
            .Where(a => IsTransitionLegal(state, a))
            .Select(a => a.ToString())
            .ToArray();

        return legal.Length == 0 ? "none" : string.Join(", ", legal);
    }

    private async Task<Subscription> GetChangeablePlanSubscriptionAsync(int subscriptionId, string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            throw new BillingConfigurationException($"Subscription {subscriptionId} does not exist.");
        }

        // Changing to the plan already in force is a no-op — reject before any provider call.
        if (subscription.PlanHandle == targetPlanHandle)
        {
            throw new InvalidPlanChangeException(subscriptionId,
                $"it is already on plan '{targetPlanHandle}'");
        }

        // A cancelled subscription must be reactivated (UC4) before its plan can change.
        if (!subscription.IsLive)
        {
            throw new InvalidPlanChangeException(subscriptionId,
                $"it is {subscription.State}. Reactivate it first.");
        }

        var target = await _billingClient.FindPlanAsync(targetPlanHandle, cancellationToken);
        if (target is null)
        {
            throw new BillingConfigurationException($"Plan handle '{targetPlanHandle}' does not resolve to a product.");
        }

        return subscription;
    }

    /// <summary>
    /// Refuses to record usage unless the configured handle really is a metered component on the
    /// family — otherwise UC2 would fail later with a confusing provider error (UC2 preconditions).
    /// </summary>
    private async Task EnsureComponentIsMeteredAsync(CancellationToken cancellationToken)
    {
        var component = await _billingClient.FindMeteredComponentAsync(_meteredComponentHandle, cancellationToken);
        if (component is null)
        {
            throw new BillingConfigurationException(
                $"Metered component handle '{_meteredComponentHandle}' does not resolve to a component.");
        }

        if (!component.IsMetered)
        {
            throw new BillingConfigurationException(
                $"Component '{_meteredComponentHandle}' is of kind '{component.Kind}', but usage can only be " +
                $"recorded against '{MeteredComponent.MeteredKind}'.");
        }
    }

    /// <summary>
    /// Eventing is best-effort: a handler that throws is logged, never allowed to undo work the
    /// provider has already accepted (plan.md §2.5).
    /// </summary>
    private async Task PublishAsync(INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.Publish(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Publishing {Notification} failed after the provider call succeeded: {Message}",
                notification.GetType().Name, ex.Message);
        }
    }

    private static string DeriveFirstName(string userName)
    {
        var localPart = userName.Split('@')[0];
        return string.IsNullOrWhiteSpace(localPart) ? userName : localPart;
    }

    private static string DeriveLastName(string userName)
    {
        var parts = userName.Split('@');
        return parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : "eShopOnWeb";
    }
}
