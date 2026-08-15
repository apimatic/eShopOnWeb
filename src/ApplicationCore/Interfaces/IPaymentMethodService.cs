using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's vaulted cards. All actions are scoped to the caller.</summary>
public interface IPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
