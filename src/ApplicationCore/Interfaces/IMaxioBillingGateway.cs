using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Gateway to Maxio Advanced Billing, the system of record for plans, customers, and subscriptions.
/// </summary>
public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(
        string reference,
        string email,
        string firstName,
        string lastName,
        string uniquenessToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string uniquenessToken,
        CancellationToken cancellationToken = default);
}
