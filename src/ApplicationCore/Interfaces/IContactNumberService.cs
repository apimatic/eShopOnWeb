using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers shoppers put on file. Every number is validated with the provider and
/// stored in its canonical form; every operation is scoped to the owning shopper.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for a shopper after the provider confirms it is a usable destination.
    /// Throws <see cref="Exceptions.InvalidPhoneNumberException"/> if the provider rejects it.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Returns the caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the caller's numbers. Returns false when no such number belongs to the caller,
    /// so one shopper can never remove another's.
    /// </summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The number to message a shopper on (their most recently registered one), or null when they have
    /// none on file — in which case they are simply not messaged.
    /// </summary>
    Task<ContactNumber?> GetReachableNumberAsync(string ownerId, CancellationToken cancellationToken = default);
}
