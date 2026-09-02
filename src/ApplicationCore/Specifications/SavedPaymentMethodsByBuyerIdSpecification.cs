using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerIdSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerIdSpecification(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }
}
