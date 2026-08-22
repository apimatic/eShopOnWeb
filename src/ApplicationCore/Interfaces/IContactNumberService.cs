using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber);

    Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId);

    Task DeleteAsync(string buyerId, int contactNumberId);

    Task<ContactNumber?> GetActiveDestinationAsync(string buyerId);
}
