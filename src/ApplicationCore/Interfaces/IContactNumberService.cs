using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validates a number with the provider and registers its canonical form for the shopper.
    /// Throws InvalidPhoneNumberException when the provider does not consider it usable.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes a number owned by the shopper. Returns false when not found (or not owned).</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}
