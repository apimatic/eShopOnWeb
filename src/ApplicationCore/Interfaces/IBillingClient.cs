using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam through which the subscription feature talks to the billing
/// provider. Nothing outside the one Infrastructure implementation of this interface may reference
/// the billing provider's SDK or wire format.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the plans configured for this integration (see UC0/UC1).</summary>
    Task<IReadOnlyList<BillingPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the configured metered usage component resolves and is of metered kind. Safe to call
    /// repeatedly - implementations should cache a successful result.
    /// </summary>
    Task ValidateUsageComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks up a customer by reference, creating one if none exists (idempotent on reference).</summary>
    Task<BillingCustomer> EnsureCustomerAsync(BillingCustomerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Looks up a customer by reference, returning null rather than creating one when absent.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to the given billing-provider customer id.</summary>
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription by id. Throws <see cref="Exceptions.SubscriptionNotFoundException"/> if it does not exist.</summary>
    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrolls the given customer in the plan identified by <paramref name="planHandle"/>.</summary>
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Records a quantity of metered usage against the configured usage component.</summary>
    Task<BillingUsage> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Reads the current period-to-date balance for the configured usage component.</summary>
    Task<BillingComponentBalance> GetUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Previews the prorated charge/credit of moving to <paramref name="targetPlanHandle"/> now.</summary>
    Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Commits an immediate, prorated plan change.</summary>
    Task<BillingSubscription> CommitPlanChangeNowAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Schedules a plan change to take effect at the next renewal, with no proration.</summary>
    Task<BillingSubscription> SchedulePlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription, either immediately or at the end of the current period.</summary>
    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
