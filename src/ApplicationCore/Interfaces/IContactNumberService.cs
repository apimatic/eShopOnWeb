using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validates a raw number with the provider and stores the provider's
    /// canonical form. Throws <see cref="Exceptions.InvalidPhoneNumberException"/>
    /// when the provider does not consider it a usable destination.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string buyerId, string rawNumber, string? countryCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the number does not exist or belongs to another shopper.</summary>
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
