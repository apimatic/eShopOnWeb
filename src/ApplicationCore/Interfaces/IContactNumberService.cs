using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. All operations are scoped to a single
/// shopper (<c>ownerId</c>): a shopper never sees, uses or deletes another's numbers.
/// </summary>
public interface IContactNumberService
{
    /// <summary>Validates and registers a number for the shopper, storing the provider's canonical form.</summary>
    /// <exception cref="Exceptions.InvalidContactNumberException">The provider does not consider the number a usable destination.</exception>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId);

    /// <summary>Removes one of the shopper's numbers. Returns false if it does not exist for this shopper.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId);
}
