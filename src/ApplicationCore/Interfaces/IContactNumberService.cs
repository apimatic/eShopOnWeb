using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ShopperContactNumber> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken);
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
}
