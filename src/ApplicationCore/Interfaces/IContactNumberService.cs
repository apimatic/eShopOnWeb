using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the mobile numbers a shopper has on file. Every operation is scoped to the owning
/// shopper: one shopper can never see, use or delete another's number.
/// </summary>
public interface IContactNumberService
{
    /// <summary>
    /// Registers a number for the shopper after validating it with the provider and storing the
    /// provider's canonical form.
    /// </summary>
    Task<RegisterContactNumberResult> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    /// <summary>The shopper's registered numbers.</summary>
    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the shopper's numbers. Returns false when the number does not exist or is not
    /// owned by the shopper.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
