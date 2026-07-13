using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

// Provider-agnostic seam for recurring-billing operations. ApplicationCore depends only on
// this interface; the one concrete implementation (Maxio, reached over HTTP) lives in
// Infrastructure. See plan §2.2/§4.2.
public interface IBillingClient
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingComponent> GetComponentAsync(string componentHandle, CancellationToken cancellationToken = default);

    Task<int> GetComponentUnitBalanceAsync(int subscriptionId, string componentHandle, CancellationToken cancellationToken = default);

    Task<BillingUsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, int quantity, string? memo, CancellationToken cancellationToken = default);

    Task<BillingPlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ChangePlanNowAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> SchedulePlanChangeAsync(int subscriptionId, string targetProductHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelNowAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelAtEndOfPeriodAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
