using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct);
    Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken ct);
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct);
    Task<ContactNumber?> GetPreferredAsync(string buyerId, CancellationToken ct);
}
