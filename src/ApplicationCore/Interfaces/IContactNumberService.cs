using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's registered mobile contact numbers. Every operation is scoped to a single
/// shopper's own data.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper. The provider must consider it a usable destination or the
    /// registration is rejected here; the stored value is the provider's canonical form.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the shopper's numbers. Returns false if it does not exist or does not belong to
    /// the shopper. After removal nothing may be sent to it again.
    /// </summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
