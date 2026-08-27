using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validates a number with the messaging provider and registers the provider's
    /// canonical form for the shopper. Throws InvalidPhoneNumberException if the
    /// provider does not consider it a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string phoneNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a number owned by the shopper. Returns false if not found.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}
