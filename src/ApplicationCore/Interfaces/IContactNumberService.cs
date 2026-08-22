using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, string? countryCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
    Task<ContactNumber?> GetPrimaryForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<bool> IsRegisteredAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);
}
