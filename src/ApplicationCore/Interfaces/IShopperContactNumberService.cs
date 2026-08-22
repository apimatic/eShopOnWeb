using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperContactNumberService
{
    Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken);
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
}
