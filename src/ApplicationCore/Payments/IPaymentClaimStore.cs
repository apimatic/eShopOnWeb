using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Claims backed by a primary key in the application's own store: the second caller to claim a key is
/// refused by the store itself, across requests, instances and processes.
/// </summary>
public interface IPaymentClaimStore
{
    /// <summary>Returns null when the key is already claimed (and the existing claim has not expired).</summary>
    Task<PaymentClaimLease?> TryAcquireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    Task ReleaseAsync(PaymentClaimLease lease, CancellationToken cancellationToken = default);
}

public sealed record PaymentClaimLease(string Key, Guid Owner);
