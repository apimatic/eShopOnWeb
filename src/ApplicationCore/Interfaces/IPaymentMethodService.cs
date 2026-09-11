using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved (vaulted) cards. Every operation is scoped to the caller.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card for the shopper and record a safe descriptor.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove a saved card: delete it from the vault and from the caller's records.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
