using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperContactNumberService
{
    Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
    Task<ShopperContactNumber?> GetLatestForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<bool> IsRegisteredAsync(string buyerId, string canonicalNumber, CancellationToken cancellationToken = default);
}
