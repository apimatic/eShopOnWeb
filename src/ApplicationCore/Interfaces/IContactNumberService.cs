using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every method is scoped to the calling shopper's
/// own numbers — one shopper can never see, use, or delete another's.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper. The number is validated with the provider first; a number
    /// the provider does not consider usable is rejected here, and the provider's canonical form is
    /// what gets stored.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's numbers. Returns false if it is not theirs or does not exist.</summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}
