using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. All operations are scoped to the owning shopper: one shopper
/// can never see, use, or delete another's card.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vault a card for the shopper and return a safe description of it.</summary>
    Task<SavedCardInfo> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedCardInfo>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove one of the shopper's saved cards, in the vault and locally, so it can no longer be used to pay.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);

    /// <summary>Resolve a saved card the shopper owns to its PayPal vault token id (used when paying with a saved card).</summary>
    Task<string> ResolveVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
