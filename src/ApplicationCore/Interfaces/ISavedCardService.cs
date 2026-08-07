using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. A saved card belongs to the shopper who saved it; no operation
/// ever exposes, uses or deletes another shopper's card.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card in PayPal and saves the resulting token (plus safe summary) for the shopper.</summary>
    Task<PaymentMethod> SaveCardAsync(
        string identity, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetSavedCardsAsync(
        string identity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the shopper's saved cards. Returns false if it does not exist or is not theirs.
    /// After removal the card no longer appears in their list and can no longer be used to pay.
    /// </summary>
    Task<bool> DeleteSavedCardAsync(
        string identity, int paymentMethodId, CancellationToken cancellationToken = default);
}
