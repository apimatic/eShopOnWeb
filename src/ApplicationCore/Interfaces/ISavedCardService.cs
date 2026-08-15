using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Cards are vaulted at PayPal; only the vault token and safe
/// display fields are kept locally. A saved card belongs to the shopper who saved it — one shopper
/// never sees, uses or deletes another's.
/// </summary>
public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes a saved card. Afterwards it no longer appears among the caller's cards and can
    /// no longer be used to pay.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
