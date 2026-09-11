using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }
}

public class SavedPaymentMethodByIdForBuyerSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpec(int id, string buyerId)
    {
        Query.Where(m => m.Id == id && m.BuyerId == buyerId);
    }
}
