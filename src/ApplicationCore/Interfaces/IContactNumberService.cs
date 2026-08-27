using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validates the number with the provider and stores the provider's canonical form.
    /// Throws InvalidPhoneNumberException when the provider rejects the number.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes a number owned by the buyer; afterwards nothing may be sent to it.</summary>
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct = default);
}
