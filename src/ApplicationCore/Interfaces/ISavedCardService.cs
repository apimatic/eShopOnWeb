using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Every operation is scoped to the caller: one shopper
/// can never see, use or delete another's card. Full card details are never stored by this app.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card in PayPal and saves a safe reference to it for the shopper.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card so it can no longer be seen or used to pay. Returns false if not found.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
