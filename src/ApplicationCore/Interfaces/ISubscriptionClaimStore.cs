using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Durable claims that stop the same billing write being requested twice.
/// A claim is an insert the store refuses when the key already exists.
/// </summary>
public interface ISubscriptionClaimStore
{
    /// <returns>False when another claim for the same buyer and plan already exists.</returns>
    Task<bool> TryClaimEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment?> GetEnrollmentAsync(string buyerId, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task UpdateEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    /// <summary>Deletes the claim; a claim already removed by a concurrent request is not an error.</summary>
    Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    /// <returns>False when a customer claim for the buyer already exists.</returns>
    Task<bool> TryClaimCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default);

    Task<BillingCustomer?> GetCustomerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task UpdateCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Deletes the claim; a claim already removed by a concurrent request is not an error.</summary>
    Task ReleaseCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default);
}
