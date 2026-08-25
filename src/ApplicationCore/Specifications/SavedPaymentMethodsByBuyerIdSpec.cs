using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerIdSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }
}
