using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to a single
/// shopper — one shopper can never see, use, or remove another's number.
/// </summary>
public interface IShopperContactService
{
    /// <summary>
    /// Registers a number for the shopper. The number is validated with the provider up front; a
    /// number the provider does not consider a usable destination is rejected here (with an
    /// <see cref="Exceptions.InvalidContactNumberException"/>) rather than when a later message fails.
    /// The stored value is the provider's canonical form of the number.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's numbers. Returns false when it does not exist or is not theirs.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
