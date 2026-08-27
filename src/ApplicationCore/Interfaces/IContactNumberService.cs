using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validates a number with the provider and registers its canonical form
    /// for the given shopper.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string phoneNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a number owned by the given shopper. Throws NotFoundException otherwise.</summary>
    Task DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}
