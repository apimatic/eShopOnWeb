using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodByBuyerAndIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByBuyerAndIdSpec(string buyerId, int paymentMethodId)
    {
        Query.Where(m => m.BuyerId == buyerId && m.Id == paymentMethodId);
    }
}
