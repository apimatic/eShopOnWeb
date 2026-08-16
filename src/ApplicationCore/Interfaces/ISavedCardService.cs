using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Paypal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's cards, backed by PayPal's vault.</summary>
public interface ISavedCardService
{
    /// <summary>Vaults the card with PayPal and saves a safe reference for the shopper.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, SaveCardInput input, CancellationToken ct = default);

    /// <summary>Returns the shopper's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes a saved card. It can no longer be seen or used to pay afterwards.</summary>
    Task RemoveCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
