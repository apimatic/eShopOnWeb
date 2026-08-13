using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's mobile contact numbers. Every operation is scoped to the owning shopper.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper after the provider confirms it is a usable destination,
    /// storing the provider's canonical form. Throws
    /// <see cref="Exceptions.InvalidContactNumberException"/> if it is not usable.
    /// Returns the new contact number's id.
    /// </summary>
    Task<int> RegisterAsync(string buyerId, string rawNumber, CancellationToken ct = default);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes one of the shopper's numbers. Returns false if it is not theirs / not found.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}
