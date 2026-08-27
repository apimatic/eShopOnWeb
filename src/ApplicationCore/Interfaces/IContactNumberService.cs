using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validates a shopper-typed number with the provider and registers its canonical form.
    /// Throws BadRequestException when the provider does not consider it a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string phoneNumber, CancellationToken ct = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken ct = default);

    /// <summary>Removes a number owned by the shopper; throws NotFoundException when it isn't theirs.</summary>
    Task DeleteAsync(string ownerId, int contactNumberId, CancellationToken ct = default);

    /// <summary>The shopper's primary (earliest-registered) number, or null when none is on file.</summary>
    Task<ContactNumber?> GetPrimaryAsync(string ownerId, CancellationToken ct = default);
}
