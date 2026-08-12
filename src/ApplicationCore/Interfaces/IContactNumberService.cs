using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's own mobile contact numbers. Every operation is scoped to a single shopper; one
/// shopper can never see, use or delete another's numbers.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a number for the shopper. The provider must consider it a usable destination, otherwise
    /// <see cref="Exceptions.InvalidPhoneNumberException"/> is thrown. The provider's canonical form is stored.
    /// Registering a number the shopper already has returns the existing record rather than duplicating it.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one of the shopper's numbers. Returns false if it does not exist or belongs to another
    /// shopper (so the caller cannot tell those two cases apart). Afterwards nothing is sent to it again.
    /// </summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
