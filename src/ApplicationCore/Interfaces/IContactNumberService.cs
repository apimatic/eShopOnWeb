using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to a single shopper
/// (<paramref name="ownerId"/>): one shopper can never see, use or delete another's numbers.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper. The number is validated and canonicalised with the
    /// provider first; a number the provider does not consider a usable destination is rejected here.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's numbers. Returns false if it was not found among their numbers.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}
