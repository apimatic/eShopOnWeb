using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Durable claims that keep a buyer from creating duplicate billing customers or subscriptions.
/// A claim is an insert the store refuses (by primary key) when one already exists.
/// </summary>
public interface ISubscriptionClaimStore
{
    Task<BillingCustomer?> GetCustomerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Inserts <paramref name="claim"/>. Returns null when the claim was taken, otherwise the row that already holds it.</summary>
    Task<BillingCustomer?> TryClaimCustomerAsync(BillingCustomer claim, CancellationToken cancellationToken = default);

    Task SaveCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default);

    Task ReleaseCustomerAsync(BillingCustomer customer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionEnrollment>> ListEnrollmentsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Inserts <paramref name="claim"/>. Returns null when the claim was taken, otherwise the row that already holds it.</summary>
    Task<SubscriptionEnrollment?> TryClaimEnrollmentAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken = default);

    /// <summary>Persists a state transition. Returns false when another request changed the row first (optimistic concurrency).</summary>
    Task<bool> TrySaveEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    Task ReleaseEnrollmentAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);
}
