using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Shopper-scoped management of the mobile numbers a shopper has on file. Every method acts
/// only on the numbers belonging to <c>buyerId</c>.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validate a raw number with the provider and, if usable, store its canonical E.164 form
    /// for the shopper. Throws <see cref="Exceptions.InvalidPhoneNumberException"/> if the
    /// provider does not consider it a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove one of the shopper's numbers. Throws <see cref="Exceptions.EntityNotFoundException"/> if it is not theirs.</summary>
    Task RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
