using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam onto the billing provider. Everything the subscription
/// feature needs from Maxio goes through this interface; the one concrete implementation
/// (<c>MaxioBillingClient</c>) lives in Infrastructure and is the only class that talks HTTP.
/// Implementations normalise provider results (cents → dollars) and surface failures as
/// <see cref="Exceptions.BillingProviderException"/> / <see cref="Exceptions.BillingConfigurationException"/>.
/// </summary>
public interface IBillingClient
{
    // ---- Plans (UC1 step 1) ----
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    // ---- Customers (UC1 idempotent customer, §4.4) ----
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task<BillingCustomer> CreateCustomerAsync(string reference, string email, CancellationToken cancellationToken = default);

    // ---- Subscriptions (UC1) ----
    Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);
    Task<CustomerSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    // ---- Usage (UC2) ----
    Task<MeteredComponentInfo> GetMeteredComponentAsync(CancellationToken cancellationToken = default);
    Task<int> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);
    Task<decimal?> GetUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default);

    // ---- Plan change (UC3) ----
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default);
    Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    // ---- Lifecycle (UC4) ----
    Task<CustomerSubscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task<CustomerSubscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);
    Task<CustomerSubscription> CancelAsync(int subscriptionId, bool immediate, string? reason, CancellationToken cancellationToken = default);
    Task<CustomerSubscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
