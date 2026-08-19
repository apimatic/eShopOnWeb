using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Typed operations against Maxio Advanced Billing. Method names and shapes follow the
/// OpenAPI operationIds in <c>maxio-spec/</c>.
/// </summary>
public interface IMaxioBillingClient
{
    Task<IReadOnlyList<BillingPlan>> ListProductsForProductFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default);

    Task<BillingCustomer?> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string productHandle, string? reference, string? paymentCollectionMethod, CancellationToken cancellationToken = default);
}
