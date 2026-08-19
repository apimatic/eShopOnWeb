using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Registration and management of a shopper's own contact numbers.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a number for the shopper. The number is validated and canonicalised with the
    /// provider up front; an unusable destination is rejected here rather than at send time.
    /// Throws <see cref="Exceptions.InvalidPhoneNumberException"/> when the provider rejects it.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string rawNumber, CancellationToken cancellationToken);

    /// <summary>The caller's own registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>Remove one of the caller's numbers. Returns false if it is not theirs / not found.</summary>
    Task<bool> RemoveAsync(string ownerId, int contactNumberId, CancellationToken cancellationToken);
}
