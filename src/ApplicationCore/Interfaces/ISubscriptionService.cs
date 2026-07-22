using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (mirrors <see cref="IOrderService"/>). Orchestrates the
/// billing client, applies domain validation, and publishes MediatR notifications on lifecycle
/// changes. The concrete <c>SubscriptionService</c> lives in ApplicationCore.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1: list the plans available to subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1: enrol the eShopOnWeb user (identified by <paramref name="userName"/>) in the chosen plan.
    /// Ensures a provider customer exists (idempotent on the user reference), returns an existing
    /// active subscription on the same plan instead of creating a duplicate, and publishes
    /// <see cref="IntegrationEvents.SubscriptionActivated"/>.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>UC1 read side: the user's subscriptions as seen by the provider.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC2: record <paramref name="quantity"/> units of metered usage against the subscription's
    /// <c>api-call</c> component and read back the running period-to-date total.
    /// </summary>
    Task<UsageResult> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC3: preview the prorated (or at-renewal) cost of moving to another plan.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3: commit the plan change, rejecting the commit if the confirmed preview has gone stale.
    /// Publishes <see cref="IntegrationEvents.SubscriptionPlanChanged"/>.
    /// </summary>
    Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, PlanChangePreview confirmedPreview, CancellationToken cancellationToken = default);

    /// <summary>UC4: pause an active subscription. Publishes <see cref="IntegrationEvents.SubscriptionStateChanged"/>.</summary>
    Task<CustomerSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC4: resume a paused (on-hold) subscription.</summary>
    Task<CustomerSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC4: cancel a subscription immediately or at the end of the current period.</summary>
    Task<CustomerSubscription> CancelAsync(int subscriptionId, bool immediate, string? reason, CancellationToken cancellationToken = default);

    /// <summary>UC4: reactivate a canceled subscription.</summary>
    Task<CustomerSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
