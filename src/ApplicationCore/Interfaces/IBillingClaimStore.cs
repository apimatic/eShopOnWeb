using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Persists the claims taken before billing-provider writes. Claims are refused by primary key,
/// so the refusal holds across requests, instances and (on a durable store) restarts.
/// </summary>
public interface IBillingClaimStore
{
    Task<BillingCustomer?> GetCustomerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment?> GetEnrollmentAsync(string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionEnrollment>> ListUnsettledEnrollmentsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Inserts the claim; returns <c>false</c> when a claim with the same key already exists.</summary>
    Task<bool> TryClaimAsync<T>(T claim, CancellationToken cancellationToken = default) where T : BillingClaim, IAggregateRoot;

    /// <summary>
    /// Saves a change to a claim (a status change, or a take-over that rotates its concurrency token).
    /// Returns <c>false</c> when another caller changed or removed the claim first.
    /// </summary>
    Task<bool> TryUpdateAsync<T>(T claim, CancellationToken cancellationToken = default) where T : BillingClaim, IAggregateRoot;

    /// <summary>Releases a claim after the provider definitively refused the write.</summary>
    Task ReleaseAsync<T>(T claim, CancellationToken cancellationToken = default) where T : BillingClaim, IAggregateRoot;
}
