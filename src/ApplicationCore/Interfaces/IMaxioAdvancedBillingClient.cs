using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Thin port over Maxio Advanced Billing REST resources used by this integration.
/// Paths and shapes are those documented by Maxio (formerly Chargify).
/// </summary>
public interface IMaxioAdvancedBillingClient
{
    string ProductFamilyHandle { get; }

    Task<IReadOnlyList<BillingProduct>> ListFamilyProductsAsync(CancellationToken cancellationToken = default);

    Task<BillingProduct?> GetProductByHandleAsync(string handle, CancellationToken cancellationToken = default);

    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<BillingCustomer> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string reference,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<BillingSubscription?> FindSubscriptionByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(
        int customerId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken = default);
}
