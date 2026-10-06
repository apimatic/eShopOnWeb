using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port over the Maxio Advanced Billing REST API. Implemented in the Infrastructure
/// layer. Maxio is the billing system of record; these operations read from / write to
/// it directly and do not maintain a parallel local copy of the data.
/// </summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<SubscriptionPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(BillingCustomerDraft draft, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(long customerId, string planHandle, string uniquenessToken, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsForCustomerAsync(long customerId, CancellationToken cancellationToken = default);
}
