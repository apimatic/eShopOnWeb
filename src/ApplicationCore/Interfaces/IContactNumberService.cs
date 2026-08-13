using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to one shopper:
/// a shopper can never see, use, or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Validates the raw number with the provider and, if it is a usable destination, stores its
    /// canonical form for the shopper. Rejects unusable numbers up front.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the caller's numbers. Returns false if no such number belongs to the caller.
    /// </summary>
    Task<bool> RemoveAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
