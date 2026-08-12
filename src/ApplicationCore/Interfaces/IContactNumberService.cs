using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Registers and manages the mobile numbers a shopper has on file. Every operation is scoped to a
/// single shopper (<paramref name="buyerId"/>): one shopper can never see, use, or delete another's.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates and canonicalizes the number with the provider, then stores its canonical E.164 form
    /// for the shopper. Throws <see cref="Exceptions.InvalidPhoneNumberException"/> if the provider does
    /// not consider it a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken ct = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes one of the shopper's numbers. Throws <see cref="Exceptions.NotFoundException"/> if it is not theirs.</summary>
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}
