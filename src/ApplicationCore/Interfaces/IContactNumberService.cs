using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. A number belongs to the shopper who
/// registered it; one shopper can never see, use, or delete another's.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper. The number is rejected here — not when a message later
    /// fails — if the provider does not consider it a usable destination, and it is stored in the
    /// provider's own canonical form rather than as typed.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListForOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the caller's numbers. Returns false when it does not exist or is not the
    /// caller's. Afterwards it no longer appears among the caller's numbers and nothing is sent to it.
    /// </summary>
    Task<bool> DeleteAsync(int contactNumberId, string ownerId, CancellationToken cancellationToken = default);
}
