using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (UC1-UC4), orchestrating the billing client and the
/// in-process notifications. Hosts call this; they never call the billing client directly.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1 - the plans a customer can choose from.</summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1 - enrolls the eShopOnWeb user in a plan, creating the provider customer if needed.
    /// Idempotent on the user reference: an already-active subscription is returned as-is.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userReference, string? planHandle, CancellationToken cancellationToken = default);

    /// <summary>UC1 - the subscriptions belonging to an eShopOnWeb user.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>UC2 - reports metered usage and reads back the running period-to-date total.</summary>
    Task<UsageReportResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC3 - quotes a plan change without committing it.</summary>
    Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3 - commits a plan change. When <paramref name="previewedPaymentDue"/> is supplied it is
    /// re-quoted first and the commit is rejected if the amount has moved.
    /// </summary>
    Task<PlanChangeResult> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, decimal? previewedPaymentDue, CancellationToken cancellationToken = default);

    /// <summary>UC4 - applies a lifecycle transition, rejecting ones that are illegal from the current state.</summary>
    Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action, CancellationTiming cancellationTiming, string? reason, CancellationToken cancellationToken = default);
}
