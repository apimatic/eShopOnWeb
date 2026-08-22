using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<Result<ShopperContactNumber>> RegisterAsync(string buyerId, string rawNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken);
}
