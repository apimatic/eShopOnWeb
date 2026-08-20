using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
    Task<string?> GetActiveDestinationAsync(string buyerId, CancellationToken cancellationToken = default);
}
