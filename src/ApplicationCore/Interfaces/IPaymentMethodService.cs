using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's vaulted cards. All operations are buyer-scoped.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a raw card for the buyer and stores only a safe description of it.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The buyer's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes the buyer's saved card so it no longer appears and can no longer be used to pay.</summary>
    Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default);
}
