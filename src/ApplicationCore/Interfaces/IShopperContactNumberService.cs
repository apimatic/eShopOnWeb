using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperContactNumberService
{
    Task<ShopperContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber);
    Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId);
    Task DeleteAsync(string buyerId, int contactNumberId);
}
