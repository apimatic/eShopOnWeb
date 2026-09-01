using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    /// <summary>Vault a card with PayPal for the shopper and store only safe display data locally.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCard card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Delete a saved card (locally and from PayPal's vault). Scoped to the owning shopper.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
