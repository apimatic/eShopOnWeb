using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Save, list and remove a shopper's vaulted cards. All operations are scoped to the caller.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and stores only a safe reference to it. Returns the saved card.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes one of the caller's saved cards, at PayPal and locally. Returns false if not found/owned.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
