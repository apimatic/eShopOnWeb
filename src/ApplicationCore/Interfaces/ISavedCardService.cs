using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Save, list and remove a shopper's cards. Cards belong to the shopper who saved them.</summary>
public interface ISavedCardService
{
    /// <summary>Vault a card for the shopper and record a safe descriptor. Returns the saved-card view.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Remove a saved card so it can no longer be seen or used to pay. Returns false if not found/owned.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
