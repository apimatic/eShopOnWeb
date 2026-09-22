using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saved-cards flow: save, list and remove a shopper's cards. All actions are shopper-scoped.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns the saved-card record (safe descriptor only).</summary>
    Task<PaymentMethod> SaveAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a saved card so it no longer appears and can no longer be used to pay. Returns false if not found for this shopper.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
