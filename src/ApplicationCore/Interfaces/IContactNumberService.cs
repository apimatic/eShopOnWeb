using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Registers, lists and removes a shopper's on-file mobile numbers. Everything is scoped to one shopper.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validate and register a number for a shopper. Rejects a number the provider does not consider a
    /// usable destination (throws <see cref="Exceptions.InvalidPhoneNumberException"/>) and stores the
    /// provider's canonical form. Registering a number the shopper already has returns the existing one.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove one of the shopper's numbers. Returns false if it is not found among that shopper's numbers.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
