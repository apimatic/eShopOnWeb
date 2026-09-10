using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saved-card (vault) flows for a shopper. All operations are scoped to the caller: one shopper must never
/// see, use, or delete another's saved cards.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vault a card for the shopper; returns the saved card (safe display details only).</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove one of the caller's saved cards. Returns false if not found / not owned by the caller.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);

    /// <summary>Resolve a saved card's vault id for the caller (used when paying with a saved card).</summary>
    Task<string?> GetVaultIdForCallerAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
