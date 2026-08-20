using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    Task<System.Collections.Generic.IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}
