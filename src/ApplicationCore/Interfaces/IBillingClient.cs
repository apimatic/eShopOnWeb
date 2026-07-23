using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam to the recurring-billing platform (§2.2). Exactly one Infrastructure
/// class implements this, and nothing else in the application talks to the provider directly.
/// Every operation surfaces failures as
/// <see cref="Exceptions.BillingProviderException"/>.
/// </summary>
public interface IBillingClient
{
    /// <summary>
    /// The handle of the pay-as-you-go component this deployment bills usage against (UC2).
    /// Configuration-driven, so the domain never names a provider-specific identifier itself.
    /// </summary>
    string MeteredComponentHandle { get; }

    /// <summary>Lists the recurring plans available in the configured product family (UC1, step 1).</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a plan by its durable handle, or null when the handle does not resolve.</summary>
    Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Finds the provider-side customer for an eShopOnWeb user reference, or null.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider-side customer for this eShopOnWeb user, creating it if absent.
    /// Idempotent on <paramref name="userReference"/> so repeated subscribe calls are safe (UC1, step 3).
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string userReference, string email, string firstName, string lastName,
        CancellationToken cancellationToken = default);

    /// <summary>Enrols a customer in a plan and returns the resulting subscription (UC1, step 4).</summary>
    Task<Subscription> CreateSubscriptionAsync(string userReference, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to an eShopOnWeb user; empty when the user has none.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription, or null when no subscription has that id.</summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Resolves a component on the configured family by handle, or null (UC2 precondition).</summary>
    Task<MeteredComponent?> GetComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Reports consumption of a metered component against a subscription (UC2, step 2).</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the running period-to-date balance, or null when the component is not on the subscription.</summary>
    Task<UsageSummary?> GetUsageSummaryAsync(int subscriptionId, string componentHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Computes what a plan change would cost, without applying it (UC3, step 2).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change with the chosen timing (UC3, step 4).</summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Temporarily stops billing; the subscription can later be resumed (UC4).</summary>
    Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Returns a paused subscription to active (UC4).</summary>
    Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription now or at the period boundary (UC4).</summary>
    Task<Subscription> CancelAsync(int subscriptionId, CancellationTiming timing, string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Restarts a cancelled subscription (UC4).</summary>
    Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
