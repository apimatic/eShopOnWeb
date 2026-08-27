using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    Task<SavedCard> SaveCardAsync(string buyerId, GatewayCardDetails card);
    Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId);
    Task DeleteSavedCardAsync(string buyerId, int savedCardId);
}
