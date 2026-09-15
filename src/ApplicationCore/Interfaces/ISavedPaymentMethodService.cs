using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentSource card);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId);
    Task DeleteAsync(int paymentMethodId, string buyerId);
    Task<SavedPaymentMethod> GetOwnedAsync(int paymentMethodId, string buyerId);
}
