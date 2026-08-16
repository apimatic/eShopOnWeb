using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Save, list and remove a shopper's cards. Cards are vaulted at PayPal; only a safe descriptor is kept here.</summary>
public interface ISavedCardService
{
    Task<SavedCardView> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken ct);

    Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove the caller's saved card. Returns false if the caller has no such card.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
