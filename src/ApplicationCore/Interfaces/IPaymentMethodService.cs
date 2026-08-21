using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saved (vaulted) card management, always scoped to the signed-in shopper.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card with PayPal and persist a safe reference for the shopper.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Remove a saved card: deletes the PayPal vault token and the local reference.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
