using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saved-card management for a shopper, backed by the PayPal vault.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card and save it for the shopper.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, SaveCardInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove a saved card (also deleting its vault token so it can no longer pay).</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
