using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's mobile contact numbers. Every operation acts only on the caller's own
/// numbers — one shopper can never see, use, or delete another's.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Register a number for the shopper. The number is validated with the provider first: one it
    /// does not consider a usable destination is rejected here, and what gets stored is the
    /// provider's canonical form, not whatever the caller typed.
    /// </summary>
    Task<ContactNumberRegistrationResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>The caller's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove one of the caller's numbers. Returns false if it is not the caller's or does not exist.</summary>
    Task<bool> DeleteAsync(int contactNumberId, string buyerId, CancellationToken cancellationToken = default);
}
