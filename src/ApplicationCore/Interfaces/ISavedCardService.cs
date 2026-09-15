using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. A saved card belongs to the shopper who saved it; one shopper
/// can never see, use or delete another's.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card for the buyer and stores only a safe descriptor + the vault token.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>Lists the buyer's own saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the buyer's own saved cards. Returns false if they have no such card.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
