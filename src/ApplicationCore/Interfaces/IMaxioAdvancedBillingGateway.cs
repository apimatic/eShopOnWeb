using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// HTTP gateway to Maxio Advanced Billing. Maxio remains the system of record;
/// this interface does not persist billing state locally.
/// </summary>
public interface IMaxioAdvancedBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListConfiguredFamilyProductsAsync(CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);

    Task<CustomerSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string? subscriptionReference,
        string uniquenessToken,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    Task<CustomerSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
}
