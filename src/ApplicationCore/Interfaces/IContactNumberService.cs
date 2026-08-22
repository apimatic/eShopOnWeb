using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);

    Task<ContactNumber?> GetPreferredForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveForBuyerAsync(string buyerId, string canonicalNumber, CancellationToken cancellationToken = default);
}
