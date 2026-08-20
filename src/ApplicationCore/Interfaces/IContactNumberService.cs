using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<Result<ShopperContactNumber>> RegisterAsync(string buyerId, string phoneNumber);
    Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId);
    Task<Result> DeleteAsync(string buyerId, int contactNumberId);
}
