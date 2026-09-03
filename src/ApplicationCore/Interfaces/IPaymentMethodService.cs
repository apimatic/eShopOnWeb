using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Every operation is scoped to the caller: one shopper never sees, uses,
/// or deletes another's card. Full card details are never stored or returned.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns its safe description.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes one of the caller's saved cards. After this it no longer appears nor can be used to
    /// pay. Returns false if the card was not found for this caller.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
