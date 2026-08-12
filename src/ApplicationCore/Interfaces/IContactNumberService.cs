using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to a single owner
/// so one shopper can never see, use or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the owner. The number is validated and canonicalised with the provider
    /// first; an unusable destination is rejected here (<see cref="Exceptions.InvalidPhoneNumberException"/>)
    /// rather than at send time. What is stored is the provider's canonical form.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The owner's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the owner's numbers. Returns false if it does not exist or is not theirs.</summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}
