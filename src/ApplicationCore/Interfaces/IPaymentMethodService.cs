using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A shopper's vaulted cards. Cards belong to the shopper who saved them: every operation is
/// scoped to the caller's identity.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card with PayPal and stores only its safe descriptors locally.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Deletes the card at PayPal and locally. Afterwards it can neither be listed nor used to pay.</summary>
    Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken ct = default);
}
