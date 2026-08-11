using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. All operations are scoped to the caller: a shopper never sees,
/// uses, or deletes another shopper's saved card.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns the stored, safe representation of it.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the caller's saved cards. Afterwards it no longer appears among the caller's
    /// cards and can no longer be used to pay. Returns false if the shopper has no such card.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
