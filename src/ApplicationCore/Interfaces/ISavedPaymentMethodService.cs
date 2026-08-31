using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    /// <summary>Vault a card for the shopper and remember it locally (safe display data only).</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string ownerId, GatewayCardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Remove a saved card: deletes it at the gateway and locally. Only the owner can delete.</summary>
    Task<bool> DeleteAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
