using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface, mirroring how <see cref="IOrderService"/> exposes the order flow.
/// Orchestrates validation, the billing client and in-process notifications; it never talks to the
/// provider directly.
/// </summary>
/// <remarks>
/// Where a method takes an <c>ownerReference</c>, passing the signed-in user's reference restricts the
/// operation to that user's own subscription; passing null performs no ownership check and is reserved
/// for administrator surfaces.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given eShopOnWeb user in a plan, creating the provider-side customer record if needed.
    /// Idempotent: an existing active subscription for the same user is returned rather than duplicated.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userReference, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions held by the given eShopOnWeb user.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms that the configured metered component resolves to a component of metered kind. Runs before
    /// the first usage report and refuses to record usage when the configuration is wrong.
    /// </summary>
    Task<MeteredComponentDefinition> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads what a subscription has consumed so far in the current billing period.</summary>
    Task<UsageSummary> GetUsageSummaryAsync(int subscriptionId, string? ownerReference, CancellationToken cancellationToken = default);

    /// <summary>Records usage against the given subscription's metered component.</summary>
    Task<UsageReport> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, string? ownerReference, CancellationToken cancellationToken = default);

    /// <summary>Records usage against the active subscription held by the given user.</summary>
    Task<UsageReport> RecordUsageForUserAsync(string userReference, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Previews the cost of moving a subscription to another plan.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, string? ownerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a plan change. <paramref name="confirmedAmountDue"/> is the amount the customer was shown;
    /// the change is rejected if the provider would now charge something different.
    /// </summary>
    Task<PlanChangeResult> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, decimal confirmedAmountDue, string? ownerReference, CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition, rejecting transitions that are illegal from the current state.</summary>
    Task<CustomerSubscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action, string? reason, string? ownerReference, CancellationToken cancellationToken = default);
}
