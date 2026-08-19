using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port for Maxio Advanced Billing. Implementations must talk to Maxio using
/// the documented REST API (Basic auth, subdomain host, JSON resources).
/// </summary>
public interface IMaxioBillingGateway
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(
        CreateBillingCustomer customer,
        string uniquenessToken,
        CancellationToken cancellationToken = default);

    Task<SubscriptionDetails?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDetails>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> CreateSubscriptionAsync(
        CreateBillingSubscription subscription,
        string uniquenessToken,
        CancellationToken cancellationToken = default);
}
