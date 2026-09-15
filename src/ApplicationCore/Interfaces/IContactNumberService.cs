using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);

    Task<ContactNumber?> GetActiveForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<bool> IsNumberActiveForBuyerAsync(string buyerId, string canonicalPhoneNumber, CancellationToken cancellationToken = default);
}
