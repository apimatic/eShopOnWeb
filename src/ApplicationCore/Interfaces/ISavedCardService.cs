using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    /// <summary>Vault a card with the payment provider and keep only safe display data locally.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove a saved card locally and from the provider's vault. Owner-only.</summary>
    Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
