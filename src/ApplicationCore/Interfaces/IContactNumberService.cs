using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to a single owner:
/// one shopper never sees, uses or deletes another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates a number with the provider and, if it is a usable destination, stores its canonical
    /// form against the owner. Throws <see cref="Exceptions.InvalidPhoneNumberException"/> otherwise.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber);

    /// <summary>The caller's registered numbers, newest first.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId);

    /// <summary>Removes one of the owner's numbers. Returns false if it does not exist or is not theirs.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId);
}
