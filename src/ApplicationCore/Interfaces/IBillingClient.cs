using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam between eShopOnWeb and whichever recurring-billing platform is in
/// use. This is the only abstraction the domain knows about; the single concrete implementation
/// lives in Infrastructure and is the one and only place the provider is spoken to.
/// </summary>
/// <remarks>
/// <para>
/// Every money value crossing this interface is in whole currency units (dollars). Implementations
/// are responsible for converting the provider's minor units.
/// </para>
/// <para>
/// Failures surface as <see cref="Exceptions.BillingProviderException"/> or one of its subtypes.
/// Methods documented as returning null return null for "the provider has no such entity"; every
/// other failure throws.
/// </para>
/// </remarks>
public interface IBillingClient
{
    /// <summary>Lists the non-archived recurring plans in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a single plan by its durable handle, or null when no such plan exists.</summary>
    Task<SubscriptionPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Reads the configured product family by its durable handle, or null when absent.</summary>
    Task<ProductFamily?> FindProductFamilyAsync(string familyHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider-side customer for <paramref name="details"/>, creating one only if it
    /// does not already exist. Idempotent on <see cref="BillingCustomerDetails.Reference"/>, so
    /// repeated calls for the same eShopOnWeb user never produce duplicate customers.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(BillingCustomerDetails details, CancellationToken cancellationToken = default);

    /// <summary>Finds a provider-side customer by eShopOnWeb user reference, or null when absent.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a provider-side customer, in any state.</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a subscription by identifier, or null when no such subscription exists.</summary>
    Task<Subscription?> FindSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing customer in the plan identified by <paramref name="planHandle"/>.</summary>
    Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Reads a metered component by its durable handle, or null when no such component exists.</summary>
    Task<MeteredComponent?> FindComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Records metered usage against a subscription's component.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, int componentId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the accumulated unit balance for the current billing period, or null when the
    /// subscription has no line item for that component yet.
    /// </summary>
    Task<int?> GetPeriodToDateUnitsAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default);

    /// <summary>Quotes what moving to <paramref name="targetPlanHandle"/> would cost, without applying it.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change at the requested effective time.</summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Places the subscription on hold so it stops billing until resumed.</summary>
    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Lifts a hold and returns the subscription to active billing.</summary>
    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the subscription immediately.</summary>
    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Schedules the cancellation for the end of the current billing period.</summary>
    Task<Subscription> CancelSubscriptionAtEndOfPeriodAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled or expired subscription.</summary>
    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
