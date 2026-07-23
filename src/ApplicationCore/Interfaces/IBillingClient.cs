using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam between eShopOnWeb and whichever recurring-billing platform runs
/// the billing. Exactly one implementation talks to the provider; nothing else in the
/// application does. Implementations translate provider failures into
/// <see cref="Exceptions.BillingProviderException"/> and its subtypes, and normalize all money
/// to whole currency units (dollars).
/// </summary>
public interface IBillingClient
{
    /// <summary>
    /// The handle of the metered component configured for pay-as-you-go usage. Exposed here so
    /// the domain can drive usage reporting without knowing any provider-specific configuration.
    /// </summary>
    string MeteredComponentHandle { get; }

    /// <summary>Lists the plans customers may subscribe to, in the configured product family.</summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a plan by its durable handle, or <c>null</c> when no such plan exists.</summary>
    Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Finds the provider customer keyed on an eShopOnWeb user reference, or <c>null</c>.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider customer for this eShopOnWeb user, creating it when absent. Safe to
    /// call repeatedly — it is idempotent on <paramref name="customerReference"/>.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string customerReference,
        string email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing provider customer in a plan.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(string customerReference,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription, or <c>null</c> when it does not exist.</summary>
    Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a provider customer.</summary>
    Task<IReadOnlyCollection<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a component on the configured product family, or <c>null</c> when absent.</summary>
    Task<MeteredComponent?> FindComponentByHandleAsync(string componentHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Records metered usage against a subscription's component.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        string componentHandle,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the period-to-date usage total for a subscription's component, or <c>null</c> when
    /// the component is not attached to the subscription.
    /// </summary>
    Task<decimal?> GetPeriodToDateUsageAsync(int subscriptionId,
        string componentHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Quotes the cost of moving a subscription to another plan, without committing it.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change at the requested timing and returns the updated subscription.</summary>
    Task<BillingSubscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Holds a subscription, optionally scheduling an automatic resume.</summary>
    Task<BillingSubscription> PauseAsync(int subscriptionId,
        System.DateTimeOffset? automaticallyResumeAt,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a held subscription to active.</summary>
    Task<BillingSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription now or at the end of the current billing period.</summary>
    Task<BillingSubscription> CancelAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a cancelled subscription to active.</summary>
    Task<BillingSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
