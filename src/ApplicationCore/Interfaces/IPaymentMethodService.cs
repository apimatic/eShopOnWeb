using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card);

    Task<IReadOnlyList<PaymentMethod>> GetSavedCardsAsync(string buyerId);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId);
}
