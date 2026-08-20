using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port to Maxio Advanced Billing. Implementations must talk to the Billing API only.
/// </summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string email,
        string firstName,
        string lastName,
        string uniquenessToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        CancellationToken cancellationToken = default);
}
