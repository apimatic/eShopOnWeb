using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port to the recurring-billing system of record. Implementations translate provider failures into
/// <see cref="BillingProviderException"/> / <see cref="BillingProviderRejectedException"/>.
/// </summary>
public interface IBillingGateway
{
    Task<PlanListResult> ListPlansAsync(CancellationToken ct);
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct);
    /// <summary>Creates the customer; a transport failure is settled by re-reading by reference before returning.</summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken ct);
    Task<SubscriptionInfo?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct);
    /// <summary>Creates the subscription; a transport failure is settled by re-reading by reference before returning.</summary>
    Task<SubscriptionInfo> CreateSubscriptionAsync(int customerId, string planHandle, string reference, CancellationToken ct);
    Task<IReadOnlyList<SubscriptionInfo>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct);
}
