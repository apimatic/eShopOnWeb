using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Cards are vaulted with PayPal; this app keeps only the vault token id and
/// safe display fields. All operations are scoped to the owning shopper.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card for the shopper and records it (safe fields only).</summary>
    Task<SavedCard> SaveCardAsync(string ownerId, CardDetails card, string? label, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> GetCardsAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card so it no longer appears and can no longer be used to pay. Ownership enforced.</summary>
    Task DeleteCardAsync(string ownerId, int savedCardId, CancellationToken cancellationToken = default);
}
