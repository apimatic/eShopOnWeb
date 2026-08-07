using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. All operations are scoped to the calling shopper so one
/// shopper can never see, use or delete another's saved cards.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card with PayPal and saves a safe reference for the shopper. Returns the saved card.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's saved cards.</summary>
    Task<IReadOnlyCollection<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a saved card: deletes it from PayPal's vault and from the application. Returns false if the
    /// shopper has no such saved card. Afterwards the card no longer appears in the shopper's list and can
    /// no longer be used to pay.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
