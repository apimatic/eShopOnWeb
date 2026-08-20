using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId)
            .OrderByDescending(m => m.CreatedAt);
    }
}

public class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int paymentMethodId)
    {
        Query.Where(m => m.Id == paymentMethodId);
    }
}

public class SavedPaymentMethodByBuyerAndIdSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByBuyerAndIdSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(m => m.BuyerId == buyerId && m.Id == paymentMethodId);
    }
}
