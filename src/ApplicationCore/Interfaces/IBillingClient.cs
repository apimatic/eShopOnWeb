using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam onto the billing provider (Maxio Advanced Billing). ApplicationCore
/// depends only on this interface and the plain DTOs it exposes; the one concrete implementation
/// (Infrastructure/Services/MaxioBillingClient.cs) is the only class in the solution that talks to Maxio (§2.2).
/// </summary>
public interface IBillingClient
{
    /// <summary>
    /// Resolves the configured product family, plans, and metered component by their handles and validates
    /// their shape (e.g. the metered component really is Metered kind). Never throws — used for best-effort
    /// startup validation (UC0/UC2 preconditions); failures are reported to the caller for logging only.
    /// </summary>
    Task ValidateConfigurationAsync(CancellationToken ct = default);

    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken ct = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference, CancellationToken ct = default);

    Task<BillingCustomer> EnsureCustomerAsync(string userReference, string email, CancellationToken ct = default);

    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct = default);

    Task<Subscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken ct = default);

    Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken ct = default);

    Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, int componentId, int quantity, string? memo, CancellationToken ct = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken ct = default);

    Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken ct = default);

    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken ct = default);

    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool cancelAtEndOfPeriod, string? reason, CancellationToken ct = default);

    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken ct = default);
}
