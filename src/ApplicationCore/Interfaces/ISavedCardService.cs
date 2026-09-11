using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Card numbers are vaulted at PayPal; only safe descriptors and
/// the vault reference are kept locally. A saved card belongs solely to the shopper who saved it.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card for the shopper and returns the local saved-card id.</summary>
    Task<int> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards, described safely.</summary>
    Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's saved cards so it can no longer be used to pay.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
