using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt);
    }
}

public class SavedPaymentMethodByIdAndBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndBuyerSpecification(int id, string buyerId)
    {
        Query.Where(m => m.Id == id && m.BuyerId == buyerId && !m.IsDeleted);
    }
}
