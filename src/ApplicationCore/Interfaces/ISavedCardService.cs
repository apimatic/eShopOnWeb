using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves, lists and removes a shopper's cards. A saved card belongs to the shopper who saved it;
/// one shopper never sees, uses or deletes another's. Full card details are never stored here.
/// </summary>
public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, SaveCardInput input,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card. Returns false if it wasn't the caller's card.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default);
}
