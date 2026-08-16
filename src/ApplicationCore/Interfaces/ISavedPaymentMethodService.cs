using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. All operations are scoped to the owning shopper;
/// full card details are never stored by this app.
/// </summary>
public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId);
    Task DeleteAsync(string buyerId, int paymentMethodId);
}
