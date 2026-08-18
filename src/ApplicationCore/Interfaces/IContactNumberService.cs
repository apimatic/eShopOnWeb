using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's mobile contact numbers. Every operation is scoped to the owning shopper:
/// one shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates and canonicalises the raw number with the provider and stores its canonical E.164
    /// form for the shopper. Throws <see cref="Exceptions.InvalidPhoneNumberException"/> if the
    /// provider does not consider the number a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's own numbers. Returns false if it is not theirs / not found.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
