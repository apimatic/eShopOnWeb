using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic seam for the subscription billing engine. The only concrete
/// implementation (Infrastructure/Services/MaxioBillingClient.cs) talks to Maxio;
/// nothing else in the solution may reference the billing provider's SDK directly.
/// </summary>
public interface IBillingClient
{
    Task ValidateConfigurationAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<UsageResult> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default);

    Task<int?> GetMeteredUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ChangePlanAsync(int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
