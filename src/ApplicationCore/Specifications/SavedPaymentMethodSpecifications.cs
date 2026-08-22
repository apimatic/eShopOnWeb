using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt);
    }
}

public class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(p => p.Id == paymentMethodId && p.BuyerId == buyerId);
    }
}
