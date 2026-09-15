using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Every operation is scoped to the owning shopper so
/// one shopper can never see, use, or delete another's card.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card with PayPal and records safe metadata for the shopper.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card, deleting it from the PayPal vault so it can no longer be used to pay.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Resolves the PayPal vault token id for one of the shopper's own saved cards.</summary>
    Task<string> ResolveVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
