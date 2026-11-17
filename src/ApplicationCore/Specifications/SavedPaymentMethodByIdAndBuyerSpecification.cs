using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodByIdAndBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndBuyerSpecification(int id, string buyerId)
    {
        Query.Where(p => p.Id == id && p.BuyerId == buyerId);
    }
}
