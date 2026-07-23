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
/// Orchestrates the subscription use cases, mirroring the role
/// <see cref="OrderService"/> plays for one-time purchases: validate the request, drive the
/// billing provider through <see cref="IBillingClient"/>, then announce the change in-process.
/// </summary>
/// <remarks>
/// The eShopOnWeb user to billing-customer mapping is stateless: it is resolved on every call
/// from the user's email / username, which the provider treats as the customer's unique
/// reference. Nothing is persisted locally, so there is no local view that can drift.
/// </remarks>
public class SubscriptionService : ISubscriptionService
{
    private readonly IBillingClient _billingClient;
    private readonly IPublisher _publisher;
    private readonly IAppLogger<SubscriptionService> _logger;

    public SubscriptionService(
        IBillingClient billingClient,
        IPublisher publisher,
        IAppLogger<SubscriptionService> logger)
    {
        _billingClient = billingClient;
        _publisher = publisher;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default) =>
        _billingClient.ListPlansAsync(cancellationToken);

    public async Task<Subscription> SubscribeAsync(
        SubscriptionActor actor,
        string planHandle,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));

        var userName = RequireUserName(actor);
        var plan = await RequireLivePlanAsync(planHandle, cancellationToken);

        // Creating the customer is idempotent on the user reference, so a retry after a failed
        // enrollment reuses the existing record rather than duplicating it.
        var customer = await _billingClient.EnsureCustomerAsync(
            BuildRegistration(userName),
            cancellationToken);

        // A double-clicked or repeated subscribe must never produce a second enrollment.
        var existing = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var live = existing.FirstOrDefault(subscription => subscription.IsLive);
        if (live is not null)
        {
            _logger.LogInformation(
                "{0} is already subscribed on plan {1} (subscription {2}); returning the existing subscription.",
                userName,
                live.PlanHandle,
                live.Id);

            return live;
        }

        var created = await _billingClient.CreateSubscriptionAsync(customer.Id, plan.Handle, cancellationToken);

        await PublishBestEffortAsync(new SubscriptionActivated(userName, created), cancellationToken);

        return created;
    }

    public async Task<IReadOnlyCollection<Subscription>> GetSubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<Subscription>();
        }

        return await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
    }

    public async Task<Subscription?> GetSubscriptionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));

        var subscription = await _billingClient.GetSubscriptionAsync(subscriptionId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        // A customer must not be able to probe for, or act on, another customer's subscription,
        // so a subscription they do not own is indistinguishable from one that does not exist.
        if (!IsVisibleTo(actor, subscription))
        {
            _logger.LogWarning(
                "{0} attempted to access subscription {1}, which belongs to another customer.",
                actor.UserName ?? "An unidentified actor",
                subscriptionId);

            return null;
        }

        return subscription;
    }

    public async Task<UsageReport> RecordUsageAsync(
        SubscriptionActor actor,
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        // Verified before any usage is sent: the configured handle must resolve to a component
        // that really is metered, or nothing is reported at all.
        var component = await _billingClient.GetConfiguredMeteredComponentAsync(cancellationToken);

        var subscription = await RequireSubscriptionAsync(actor, subscriptionId, cancellationToken);
        if (!subscription.IsLive)
        {
            throw new SubscriptionStateException(
                "record usage against",
                subscription.State,
                subscription.AllowedTransitions);
        }

        var record = await _billingClient.RecordUsageAsync(
            subscription.Id,
            component.Id,
            quantity,
            memo,
            cancellationToken);

        var periodToDate = await TryReadPeriodToDateAsync(subscription, component, cancellationToken);

        return new UsageReport(record, periodToDate, component.UnitPriceInCents);
    }

    public async Task<UsageReport?> RecordUsageForUserAsync(
        string userName,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NegativeOrZero(quantity, nameof(quantity));

        var customer = await _billingClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var subscriptions = await _billingClient.ListSubscriptionsForCustomerAsync(customer.Id, cancellationToken);
        var live = subscriptions.FirstOrDefault(subscription => subscription.IsLive);
        if (live is null)
        {
            return null;
        }

        return await RecordUsageAsync(
            SubscriptionActor.Customer(userName),
            live.Id,
            quantity,
            memo,
            cancellationToken);
    }

    public async Task<PlanChangePreview> PreviewPlanChangeAsync(
        SubscriptionActor actor,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default)
    {
        var subscription = await RequirePlanChangeableSubscriptionAsync(
            actor,
            subscriptionId,
            targetPlanHandle,
            cancellationToken);

        return await _billingClient.PreviewPlanChangeAsync(
            subscription.Id,
            targetPlanHandle,
            timing,
            cancellationToken);
    }

    public async Task<Subscription> ChangePlanAsync(
        SubscriptionActor actor,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        long? expectedPaymentDueInCents,
        CancellationToken cancellationToken = default)
    {
        var subscription = await RequirePlanChangeableSubscriptionAsync(
            actor,
            subscriptionId,
            targetPlanHandle,
            cancellationToken);

        if (expectedPaymentDueInCents.HasValue)
        {
            // Re-price immediately before committing: the customer is only ever charged the
            // amount they were shown, and a moved price forces a fresh preview instead.
            var current = await _billingClient.PreviewPlanChangeAsync(
                subscription.Id,
                targetPlanHandle,
                timing,
                cancellationToken);

            if (current.PaymentDueInCents != expectedPaymentDueInCents.Value)
            {
                throw new StalePlanChangePreviewException(
                    expectedPaymentDueInCents.Value,
                    current.PaymentDueInCents);
            }
        }

        var previousPlanHandle = subscription.PlanHandle;

        var updated = await _billingClient.ChangePlanAsync(
            subscription.Id,
            targetPlanHandle,
            timing,
            cancellationToken);

        await PublishBestEffortAsync(
            new SubscriptionPlanChanged(actor.UserName, previousPlanHandle, timing, updated),
            cancellationToken);

        return updated;
    }

    public async Task<Subscription> ApplyLifecycleActionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(actor, nameof(actor));

        var subscription = await RequireSubscriptionAsync(actor, subscriptionId, cancellationToken);

        // Illegal transitions are refused locally, so no provider call is attempted.
        if (!subscription.CanTransitionTo(action))
        {
            throw new SubscriptionStateException(action, subscription.State, subscription.AllowedTransitions);
        }

        var previousState = subscription.State;

        var updated = action switch
        {
            SubscriptionLifecycleAction.Pause =>
                await _billingClient.PauseSubscriptionAsync(subscription.Id, cancellationToken),
            SubscriptionLifecycleAction.Resume =>
                await _billingClient.ResumeSubscriptionAsync(subscription.Id, cancellationToken),
            SubscriptionLifecycleAction.Cancel =>
                await _billingClient.CancelSubscriptionAsync(subscription.Id, cancellationTiming, reason, cancellationToken),
            SubscriptionLifecycleAction.Reactivate =>
                await _billingClient.ReactivateSubscriptionAsync(subscription.Id, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported lifecycle action.")
        };

        await PublishBestEffortAsync(
            new SubscriptionStateChanged(actor.UserName, action, previousState, updated),
            cancellationToken);

        return updated;
    }

    /// <summary>
    /// Resolves a subscription the actor is entitled to act on, or fails with a not-found error.
    /// </summary>
    private async Task<Subscription> RequireSubscriptionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        var subscription = await GetSubscriptionAsync(actor, subscriptionId, cancellationToken);

        return subscription ?? throw new BillingProviderException(
            $"Subscription {subscriptionId} was not found.",
            statusCode: 404,
            providerErrors: null,
            innerException: null);
    }

    /// <summary>
    /// Shared UC3 validation: the subscription must exist, be owned by the actor, be live, and
    /// the target plan must be a different, live plan. All of this precedes any provider call.
    /// </summary>
    private async Task<Subscription> RequirePlanChangeableSubscriptionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken)
    {
        Guard.Against.Null(actor, nameof(actor));
        Guard.Against.NullOrWhiteSpace(targetPlanHandle, nameof(targetPlanHandle));

        var subscription = await RequireSubscriptionAsync(actor, subscriptionId, cancellationToken);

        if (string.Equals(subscription.PlanHandle, targetPlanHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Subscription {subscriptionId} is already on plan '{targetPlanHandle}'.",
                nameof(targetPlanHandle));
        }

        if (!subscription.IsLive)
        {
            throw new SubscriptionStateException("change the plan of", subscription.State, subscription.AllowedTransitions);
        }

        await RequireLivePlanAsync(targetPlanHandle, cancellationToken);

        return subscription;
    }

    /// <summary>
    /// Resolves a plan handle to a live plan, failing with a configuration error when the seed
    /// no longer matches what this deployment is configured to sell.
    /// </summary>
    private async Task<BillingPlan> RequireLivePlanAsync(string planHandle, CancellationToken cancellationToken)
    {
        var plan = await _billingClient.FindPlanByHandleAsync(planHandle, cancellationToken);

        if (plan is null)
        {
            throw new BillingConfigurationException(
                $"Plan '{planHandle}' does not exist in the billing provider. Re-seed the product family or correct the configured handle.");
        }

        if (plan.IsArchived)
        {
            throw new BillingConfigurationException(
                $"Plan '{planHandle}' is archived in the billing provider and cannot be subscribed to.");
        }

        return plan;
    }

    /// <summary>
    /// Reads the running period-to-date total. A failure here never fails the usage report — the
    /// units are already recorded, so the total is simply reported as unavailable.
    /// </summary>
    private async Task<decimal?> TryReadPeriodToDateAsync(
        Subscription subscription,
        MeteredComponent component,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _billingClient.GetPeriodToDateUsageAsync(
                subscription.Id,
                component.Id,
                subscription.CurrentPeriodStartedAt,
                subscription.CurrentPeriodEndsAt,
                cancellationToken);
        }
        catch (BillingProviderException ex)
        {
            _logger.LogWarning(
                "Recorded usage on subscription {0} but could not read back the period-to-date total: {1}",
                subscription.Id,
                ex.Message);

            return null;
        }
    }

    /// <summary>
    /// Publishes a lifecycle notification in-process. There is no durable outbox, so delivery is
    /// best-effort by design: a failing handler is logged and the completed billing action stands.
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
                "In-process publication of {0} failed after the billing action succeeded: {1}",
                notification.GetType().Name,
                ex.Message);
        }
    }

    private static bool IsVisibleTo(SubscriptionActor actor, Subscription subscription) =>
        actor.IsAdministrator ||
        string.Equals(subscription.CustomerReference, actor.UserName, StringComparison.OrdinalIgnoreCase);

    private static string RequireUserName(SubscriptionActor actor) =>
        actor.UserName ?? throw new ArgumentException(
            "Subscribing requires a customer actor identifying the eShopOnWeb user to enrol.",
            nameof(actor));

    /// <summary>
    /// Builds the provider-side customer details for an eShopOnWeb user. The user's email /
    /// username is both the contact address and the unique reference the provider deduplicates on.
    /// </summary>
    private static BillingCustomerRegistration BuildRegistration(string userName)
    {
        var localPart = userName.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? userName : localPart;

        return new BillingCustomerRegistration(
            Reference: userName,
            Email: userName,
            FirstName: firstName,
            LastName: "eShopOnWeb");
    }
}
