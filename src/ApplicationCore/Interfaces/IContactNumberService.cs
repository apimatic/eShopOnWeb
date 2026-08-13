using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Registers and removes the mobile numbers a shopper puts on file. Registration validates the number
/// with the provider and stores the provider's canonical form; a number that is not a usable
/// destination is rejected here rather than when a later message fails to go out.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers <paramref name="rawNumber"/> for <paramref name="ownerId"/>, returning the stored
    /// contact number. Throws when the provider does not consider the number a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);
}
