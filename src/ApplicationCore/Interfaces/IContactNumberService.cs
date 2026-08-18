using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validate <paramref name="rawPhoneNumber"/> with the provider and, if it is a usable destination,
    /// store its canonical form for the shopper. Returns null when the provider does not consider it usable.
    /// Throws <see cref="Exceptions.SmsProviderException"/> if the provider could not be reached.
    /// </summary>
    Task<ContactNumber?> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove one of the shopper's own numbers. Returns false if it is not theirs / does not exist.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
