using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Every operation is scoped to the owning buyer so one shopper
/// can never see, use, or delete another's cards. Card data lives only in PayPal's vault.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the buyer and stores its token plus a safe descriptor.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The buyer's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes the buyer's saved card from PayPal's vault and this app. Returns false if not found.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
