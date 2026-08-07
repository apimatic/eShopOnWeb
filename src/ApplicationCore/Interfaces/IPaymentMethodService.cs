using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Cards are always scoped to the owning buyer, so one
/// shopper can never see, use, or delete another's.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card at the provider and saves a safe reference to it for the buyer.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>Returns the buyer's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the buyer's saved cards. Returns false if it was not theirs / did not exist.</summary>
    Task<bool> DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
