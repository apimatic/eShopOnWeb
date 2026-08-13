using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to a single owner;
/// one shopper can never see, use, or delete another shopper's numbers.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates a raw number against the provider and, if usable, stores the provider's canonical
    /// form for the owner. Throws <see cref="Exceptions.PhoneNumberValidationException"/> if the
    /// provider does not consider the number a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the owner's numbers and calls off any not-yet-sent messages to it, so nothing
    /// is ever sent to a deleted number again. Throws <see cref="Exceptions.NotFoundException"/> if the
    /// number does not exist for this owner.
    /// </summary>
    Task DeleteAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken = default);
}
