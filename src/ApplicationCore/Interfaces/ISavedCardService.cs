using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's vaulted cards. Every operation is scoped to the caller: one shopper never sees,
/// uses, or deletes another's card.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card for the buyer and returns its safe descriptor + new payment-method id.</summary>
    Task<SavedCardView> SaveAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The buyer's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes the buyer's saved card (from the vault and the store) so it is no longer usable.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
