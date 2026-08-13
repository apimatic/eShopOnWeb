using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. All operations are scoped to a single shopper
/// so one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a raw number for a shopper. The provider validates it up front: a number the provider does
    /// not consider a usable destination is rejected here (returns null), and what is stored is the
    /// provider's own canonical form, not the raw string. Registering an already-registered number is idempotent.
    /// </summary>
    Task<ContactNumber?> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove one of the shopper's own numbers. Returns false if it is not theirs or does not exist.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
