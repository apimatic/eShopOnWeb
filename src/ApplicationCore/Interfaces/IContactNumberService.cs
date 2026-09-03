using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's on-file mobile contact numbers. Every method is scoped to one shopper.</summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a number for the shopper. The number is validated with the provider first; an unusable
    /// destination is rejected here (<see cref="Microsoft.eShopWeb.ApplicationCore.Exceptions.InvalidContactNumberException"/>),
    /// and what is stored is the provider's canonical form. A number the shopper already has on file is
    /// returned as-is rather than duplicated.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove one of the caller's numbers. Returns false when it is not the caller's / not found.</summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}
