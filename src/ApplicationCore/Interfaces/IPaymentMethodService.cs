using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's vaulted cards (Flow 2). Everything is scoped to the caller.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns the stored (safe) record.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalRawCard card, CancellationToken cancellationToken);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Removes one of the caller's saved cards. Returns false if it is not theirs / not found.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
