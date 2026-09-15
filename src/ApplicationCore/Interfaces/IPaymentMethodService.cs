using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's cards via the PayPal Vault. Scoped to the owning shopper.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card for the shopper and persist only its safe description. Full number is never stored.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards, newest first.</summary>
    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove a saved card (deleting the PayPal vault token) so it can no longer be seen or used.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
