using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentSource card);
    Task<IReadOnlyList<SavedPaymentMethod>> ListForBuyerAsync(string buyerId);
    Task DeleteAsync(string buyerId, int paymentMethodId);
    Task<SavedPaymentMethod> GetOwnedAsync(string buyerId, int paymentMethodId);
}
