using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's on-file mobile contact numbers.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates and canonicalises a number with the provider, then stores its canonical
    /// form for the shopper. Throws <see cref="Exceptions.PhoneNumberValidationException"/>
    /// if the provider does not consider the number a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's numbers. Returns false if it is not theirs / does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
