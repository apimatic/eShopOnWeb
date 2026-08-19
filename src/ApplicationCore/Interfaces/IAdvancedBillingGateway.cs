using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Contract for the billing system of record. Implementations must follow the
/// Maxio Advanced Billing OpenAPI specification.
/// </summary>
public interface IAdvancedBillingGateway
{
    Task<IReadOnlyList<BillingProduct>> ListCatalogPlansAsync(CancellationToken cancellationToken);

    Task<BillingProduct?> ReadProductByHandleAsync(string productHandle, CancellationToken cancellationToken);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<BillingCustomer> CreateCustomerAsync(CreateBillingCustomer customer, CancellationToken cancellationToken);

    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    Task<BillingSubscription> CreateSubscriptionAsync(CreateBillingSubscription subscription, CancellationToken cancellationToken);
}
