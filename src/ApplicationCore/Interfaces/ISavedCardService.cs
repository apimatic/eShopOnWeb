using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's vaulted cards. All operations are scoped to the caller: one shopper can never see,
/// use, or delete another's card.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card at PayPal for the shopper and records a safe reference locally.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, string? label, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> GetCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes one of the caller's saved cards from PayPal's vault and locally.</summary>
    Task RemoveCardAsync(string buyerId, int savedCardId, CancellationToken ct);
}
