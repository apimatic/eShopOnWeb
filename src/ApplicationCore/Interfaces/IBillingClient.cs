using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam to the recurring-billing provider. This is the <b>only</b> abstraction
/// through which the application talks to billing; exactly one Infrastructure implementation exists.
/// </summary>
/// <remarks>
/// <para>
/// Every method converts provider failures into
/// <see cref="Exceptions.BillingProviderException"/>; no provider SDK type and no
/// <see cref="System.Net.Http.HttpClient"/> concern crosses this boundary.
/// </para>
/// <para>
/// All money is expressed in minor units (cents) so that no rounding happens at the seam.
/// </para>
/// </remarks>
public interface IBillingClient
{
    /// <summary>
    /// Lists the recurring plans available in the configured product family, newest price first is
    /// not guaranteed — order is the provider's. Archived plans are excluded.
    /// </summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a plan by its durable handle. Returns <c>null</c> when no plan with that handle
    /// exists, so a stale configuration surfaces as a configuration error rather than an enrollment
    /// against a guessed plan.
    /// </summary>
    Task<BillingPlan?> FindPlanByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a component on the configured product family by handle, so the caller can verify it
    /// is of metered kind before recording usage. Returns <c>null</c> when the handle does not
    /// resolve.
    /// </summary>
    Task<MeteredComponent?> FindComponentByHandleAsync(string handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the provider-side customer for a stable eShopOnWeb user reference. Returns
    /// <c>null</c> when no such customer exists — this is not an error.
    /// </summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the provider-side customer record for an eShopOnWeb user, keyed on
    /// <paramref name="reference"/>.
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(string reference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing provider-side customer in the plan identified by handle.</summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(int customerId,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to one provider-side customer.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one subscription. Returns <c>null</c> when the id is unknown to the provider.
    /// </summary>
    Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records metered usage against a subscription's component, addressed by handle.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        string componentHandle,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the running period-to-date balance for one component on one subscription. Returns
    /// <c>null</c> when the subscription has no such component.
    /// </summary>
    /// <param name="component">
    /// The already-validated component definition. Its unit price is carried through to the summary
    /// so the estimated charge needs no second provider round-trip.
    /// </param>
    Task<ComponentUsageSummary?> GetComponentUsageAsync(int subscriptionId,
        MeteredComponent component,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the provider what a plan change would cost, without committing anything. For
    /// <see cref="PlanChangeTiming.AtNextRenewal"/> no proration applies and the preview reports the
    /// new plan's price effective from the next period.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string currentPlanHandle,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change with the chosen timing.</summary>
    Task<CustomerSubscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Places a subscription on hold, optionally scheduling an automatic resumption.</summary>
    Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId,
        DateTimeOffset? automaticallyResumeAt,
        CancellationToken cancellationToken = default);

    /// <summary>Resumes a subscription that is on hold.</summary>
    Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription, either immediately or at the end of the current period.</summary>
    Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled subscription.</summary>
    Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
