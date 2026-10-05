using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Atomic claims for payment actions. A claim is a row whose key is the primary key, so the store — not
/// a read-then-write check — refuses the second caller.
/// </summary>
public interface IPaymentClaimStore
{
    /// <summary>True when this caller now owns <paramref name="key"/>; false when someone already claimed it.</summary>
    Task<bool> TryClaimAsync(string key, CancellationToken cancellationToken);

    /// <summary>When <paramref name="key"/> was claimed, or null when it is not claimed.</summary>
    Task<DateTimeOffset?> GetClaimedAtAsync(string key, CancellationToken cancellationToken);

    /// <summary>Frees a claim after PayPal definitively refused the action, so it can be tried again.</summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken);
}
