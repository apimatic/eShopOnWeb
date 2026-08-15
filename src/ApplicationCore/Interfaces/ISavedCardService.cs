using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.VaultAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves, lists and removes a shopper's cards. Cards live in PayPal's vault; this app keeps only a
/// safe descriptor and the vault token. Every operation is scoped to the owning shopper.
/// </summary>
public interface ISavedCardService
{
    Task<VaultedCard> SaveCardAsync(string buyerId, CardDetails card, string? label, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VaultedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove a saved card. Throws NotFound if it is not the caller's.</summary>
    Task DeleteCardAsync(string buyerId, int cardId, CancellationToken cancellationToken = default);
}
