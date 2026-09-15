using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentRequest card);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId);

    Task DeleteAsync(string buyerId, int paymentMethodId);
}
