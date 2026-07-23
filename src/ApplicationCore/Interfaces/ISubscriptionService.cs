using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface (UC1–UC4). Hosts orchestrate and present; all validation,
/// provider sequencing and eventing lives behind this interface.
/// </summary>
/// <remarks>
/// Customer-scoped methods take the stable eShopOnWeb <c>userReference</c> (the signed-in user's
/// email/username) and refuse to act on a subscription that reference does not own. The
/// <c>ForAnyCustomer</c> overloads skip that ownership check and are only ever reached from an
/// administrator-guarded surface.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a customer may subscribe to (UC1, step 1).</summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls an eShopOnWeb user in a plan (UC1). Creating the provider-side customer is idempotent
    /// on <paramref name="userReference"/>, and an already-active subscription on the same plan is
    /// returned rather than duplicated.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userReference,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to an eShopOnWeb user (UC1, success state).</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the running period-to-date usage of the configured metered component on a subscription
    /// the user owns. Returns <c>null</c> when the provider does not report the component.
    /// </summary>
    Task<ComponentUsageSummary?> GetUsageAsync(string userReference,
        int subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered usage against a subscription the user owns and reads back the running total
    /// (UC2).
    /// </summary>
    Task<UsageReport> RecordUsageAsync(string userReference,
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>Records metered usage against any subscription. Administrator surface only (UC2).</summary>
    Task<UsageReport> RecordUsageForAnyCustomerAsync(int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one unit of usage against the user's active subscription, if they have one. Used by
    /// the automatic "one order placed → one billable unit" hook; returns <c>null</c> when the user
    /// has no active subscription, which is the normal case for most shoppers and is not an error.
    /// </summary>
    Task<UsageReport?> TryRecordUsageForUserAsync(string userReference,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>Computes the prorated cost of a plan change without committing it (UC3, step 2).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a plan change (UC3, step 4). <paramref name="confirmedPaymentDueInCents"/> is the
    /// amount the customer was shown; if the provider now quotes a different amount the commit is
    /// refused with a <see cref="Exceptions.StalePlanChangePreviewException"/>.
    /// </summary>
    Task<CustomerSubscription> ChangePlanAsync(string userReference,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        long confirmedPaymentDueInCents,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition to a subscription the user owns (UC4).</summary>
    Task<CustomerSubscription> ApplyLifecycleActionAsync(string userReference,
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition to any subscription. Administrator surface only (UC4).</summary>
    Task<CustomerSubscription> ApplyLifecycleActionForAnyCustomerAsync(int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default);
}
