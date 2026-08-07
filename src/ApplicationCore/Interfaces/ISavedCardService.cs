using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards (PayPal Vault tokens). All operations are scoped to <c>buyerId</c>
/// so one shopper can never see, use or delete another's saved cards. Full card details are never
/// stored in the application database.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card with PayPal and saves a safe reference to it for the shopper.</summary>
    Task<PaymentMethod> SaveCardAsync(
        string buyerId,
        CardDetails card,
        string? alias,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the shopper's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card: deletes it from PayPal's Vault and from the shopper's list.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
