using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's vaulted cards. Full card details go straight to PayPal's vault and are
/// never stored by this application; only the token id and a safe display are kept, scoped to
/// the owning shopper.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card for the shopper and records a safe reference to it.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, PayPalCardInput card, CancellationToken ct = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// Removes one of the caller's saved cards from both the app and PayPal's vault, so it no
    /// longer appears in the list and can no longer be used to pay.
    /// </summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
