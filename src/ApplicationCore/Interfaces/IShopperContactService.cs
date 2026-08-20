using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperContactService
{
    Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, string? countryCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
