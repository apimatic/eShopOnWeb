using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam onto the recurring-billing engine. Exactly one Infrastructure
/// implementation talks to the provider; nothing else in the application does.
/// Every method throws a <see cref="Exceptions.BillingProviderException"/> subtype on failure.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available in the configured product family.</summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a single plan by its stable handle, or null when no such plan exists.</summary>
    Task<BillingPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Finds the provider customer for an eShopOnWeb user reference, or null when there is none yet.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>Creates a provider customer keyed on the eShopOnWeb user reference.</summary>
    Task<BillingCustomer> CreateCustomerAsync(string userReference, string email, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a provider customer.</summary>
    Task<IReadOnlyCollection<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a subscription, or null when the id is unknown to the provider.</summary>
    Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing provider customer in a plan.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Reads a component defined on the product family by handle, or null when there is none.</summary>
    Task<BillingComponent?> GetComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Reports consumption of a metered component against a subscription.</summary>
    Task<BillingUsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Reads the running period-to-date balance of a component on a subscription, or null when it is not attached.</summary>
    Task<BillingUsageTotal?> GetUsageTotalAsync(int subscriptionId, string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Quotes the cost of moving a subscription to another plan without committing it.</summary>
    Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change with the requested timing.</summary>
    Task<BillingSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Temporarily stops billing a subscription.</summary>
    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a paused subscription.</summary>
    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription immediately.</summary>
    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Schedules a subscription to cancel at the end of the current billing period.</summary>
    Task<BillingSubscription> CancelSubscriptionAtEndOfPeriodAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Restarts a cancelled subscription.</summary>
    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
