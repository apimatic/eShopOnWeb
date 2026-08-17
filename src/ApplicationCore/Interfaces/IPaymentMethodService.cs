using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saved-card (vault) operations, always scoped to the owning shopper.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card for the shopper and persist a safe reference to it.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId);

    /// <summary>Remove one of the shopper's saved cards (from PayPal's vault and this app).
    /// Returns false if the card does not exist or is not the shopper's.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId);
}
