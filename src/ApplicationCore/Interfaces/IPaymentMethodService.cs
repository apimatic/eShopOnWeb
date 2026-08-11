using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Every operation is scoped to the calling shopper; one shopper can never
/// see, use, or delete another's saved card.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card at PayPal and save its token plus a safe descriptor for the shopper.</summary>
    Task<SavedPaymentMethod> SaveAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    /// <summary>List the caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove a saved card: delete the vault token at PayPal and the record here.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
