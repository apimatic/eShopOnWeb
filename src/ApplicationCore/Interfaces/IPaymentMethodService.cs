using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves and removes a shopper's cards (vaulted at PayPal), scoped to the shopper who owns them.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card for the shopper and store only its token and a safe description.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken ct = default);

    /// <summary>
    /// Remove one of the shopper's saved cards. Afterwards it no longer appears among their cards and can
    /// no longer be used to pay. A card owned by another shopper is not visible and cannot be deleted.
    /// </summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
