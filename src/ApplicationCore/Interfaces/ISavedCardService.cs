using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved (vaulted) cards.</summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card with PayPal and saves a safe descriptor for the shopper. Returns the saved card.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, string alias, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Returns the caller's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card (from PayPal's vault and this app). Returns false if it was not the caller's.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
