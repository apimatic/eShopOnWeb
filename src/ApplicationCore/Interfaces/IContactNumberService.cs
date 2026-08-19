using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to the owner:
/// one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the owner after the provider confirms it is a usable destination,
    /// storing the provider's canonical form. Throws when the provider rejects the number.
    /// </summary>
    Task<ContactNumberView> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The owner's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumberView>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the owner's numbers. Returns false if it is not the owner's / not found.</summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}
