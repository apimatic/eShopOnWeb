using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    /// <summary>Vaults a card with PayPal for the buyer and stores only its safe display attributes.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken ct);

    Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a buyer's saved card locally and from PayPal's vault.</summary>
    Task DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken ct);
}
