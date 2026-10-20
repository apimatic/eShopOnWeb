using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Durable claims that guard billing-provider writes against duplicate submission.
/// Every refusal here is enforced by the store (primary key / concurrency token), never by a read-then-write.
/// </summary>
public interface IBillingClaimStore
{
    Task<BillingClaim?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new Pending claim. Returns false when the store already holds one for the key.</summary>
    Task<bool> TryClaimAsync(BillingClaim claim, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-arms a stale (Unknown, or expired Pending) claim for this caller. Returns false when another
    /// caller changed the claim since it was read.
    /// </summary>
    Task<bool> TryTakeOverAsync(BillingClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task CompleteAsync(BillingClaim claim, int providerId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task MarkUnknownAsync(BillingClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Deletes the claim after the provider definitively refused the write, so a corrected retry can proceed.</summary>
    Task ReleaseAsync(BillingClaim claim, CancellationToken cancellationToken = default);
}
