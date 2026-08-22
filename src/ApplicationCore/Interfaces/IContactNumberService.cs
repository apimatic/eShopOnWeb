using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken);

    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);

    Task<ContactNumber?> GetLatestForBuyerAsync(string buyerId, CancellationToken cancellationToken);
}
