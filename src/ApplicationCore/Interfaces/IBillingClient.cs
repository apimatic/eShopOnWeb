using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam to the recurring-billing platform (§2.2). Exactly one Infrastructure
/// implementation touches the provider; nothing else in the application talks to it directly.
/// </summary>
/// <remarks>
/// Every member surfaces failures as
/// <see cref="Exceptions.BillingProviderException"/>, so callers never see transport-level types.
/// Lookups that can legitimately find nothing return <c>null</c> or an empty collection instead of
/// throwing.
/// </remarks>
public interface IBillingClient
{
    /// <summary>
    /// Lists the plans available to subscribe to, within the configured product family (UC1 step 1).
    /// Returns an empty collection when the family holds no plans.
    /// </summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a plan by its stable handle. Returns <c>null</c> when no such plan exists, which
    /// callers translate into a configuration error pointing back at UC0.
    /// </summary>
    Task<BillingPlan?> FindPlanByHandleAsync(string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the provider-side customer for an eShopOnWeb user reference, or <c>null</c> if absent.
    /// </summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider-side customer for this user, creating it only if it does not already
    /// exist. Idempotent on <see cref="EnsureCustomerRequest.Reference"/>, so retrying a failed
    /// subscribe never produces a duplicate customer (UC1 failure scenarios).
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(EnsureCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Enrolls a customer in a plan (UC1 step 4).</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads one subscription, or <c>null</c> when the provider has no such subscription.</summary>
    Task<BillingSubscription?> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to a customer, including inactive ones. Returns an empty
    /// collection for a customer with no subscriptions.
    /// </summary>
    Task<IReadOnlyCollection<BillingSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a component on the configured product family by handle, or <c>null</c> when absent.
    /// UC2 uses this to verify the usage component exists and is of metered kind before recording.
    /// </summary>
    Task<BillingComponent?> FindComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the metered component this integration is configured to record usage against, and
    /// verifies it is of metered kind (UC2's precondition). Throws
    /// <see cref="Exceptions.BillingConfigurationException"/> when the configured handle does not
    /// resolve or resolves to a non-metered component, so usage is refused rather than mis-billed.
    /// </summary>
    /// <remarks>
    /// The provider configuration lives entirely behind this seam, which is why the caller does not
    /// supply the handle: ApplicationCore stays free of provider configuration (§2.2).
    /// </remarks>
    Task<BillingComponent> GetUsageComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>Records metered usage against a subscription (UC2 step 2).</summary>
    Task<long> RecordUsageAsync(RecordUsageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the running period-to-date unit total for a component on a subscription (UC2 step 3).
    /// Returns <c>null</c> when the component has never accrued usage on this subscription.
    /// </summary>
    Task<int?> GetPeriodToDateUnitsAsync(long subscriptionId, long componentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the cost of moving a subscription to another plan without committing anything
    /// (UC3 step 2).
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(long subscriptionId, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change at the requested time (UC3 step 4).</summary>
    Task<BillingSubscription> ChangePlanAsync(long subscriptionId, string targetProductHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Pauses an active subscription (UC4).</summary>
    Task<BillingSubscription> PauseAsync(long subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a paused subscription (UC4).</summary>
    Task<BillingSubscription> ResumeAsync(long subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription immediately or at the end of the period (UC4).</summary>
    Task<BillingSubscription> CancelAsync(long subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled subscription (UC4).</summary>
    Task<BillingSubscription> ReactivateAsync(long subscriptionId, CancellationToken cancellationToken = default);
}
