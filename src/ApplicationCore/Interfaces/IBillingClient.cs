using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic seam onto the recurring-billing provider. This is the single
/// abstraction ApplicationCore depends on; the concrete provider client lives in
/// Infrastructure and is the only place that talks to the provider's SDK/HTTP API.
/// </summary>
public interface IBillingClient
{
    Task<BillingProductFamily> GetProductFamilyAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingPlan> GetPlanByHandleAsync(string productHandle, CancellationToken cancellationToken = default);

    Task<BillingMeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Find-or-create, idempotent on <paramref name="reference"/>.</summary>
    Task<BillingCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<BillingSubscription?> FindLiveSubscriptionAsync(int customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<UsageRecord> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>The accumulated period-to-date usage total for the configured metered component.</summary>
    Task<int> GetMeteredUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CommitPlanChangeNowAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
