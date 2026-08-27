using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    /// <summary>
    /// Validates a mobile number with the provider and registers the provider's
    /// canonical form of it for the given shopper.
    /// Throws InvalidPhoneNumberException when the provider rejects the number.
    /// </summary>
    Task<ContactNumber> RegisterAsync(string ownerId, string phoneNumber);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string ownerId);

    /// <summary>
    /// Removes a number owned by the given shopper. Returns false when no such
    /// number exists for that shopper.
    /// </summary>
    Task<bool> DeleteAsync(string ownerId, int contactNumberId);
}
