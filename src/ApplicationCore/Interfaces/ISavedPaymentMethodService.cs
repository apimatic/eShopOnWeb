using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards (Flow 2). Every operation is scoped to the caller's own cards.
/// </summary>
public interface ISavedPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and records a safe summary of it.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken);

    /// <summary>Lists the shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Removes one of the shopper's saved cards from the vault and from the app.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
