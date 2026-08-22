using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId, bool includeDeleted = false)
    {
        Query.Where(m => m.BuyerId == buyerId);

        if (!includeDeleted)
        {
            Query.Where(m => !m.IsDeleted);
        }

        Query.OrderByDescending(m => m.CreatedAt);
    }
}

public class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int paymentMethodId)
    {
        Query.Where(m => m.Id == paymentMethodId);
    }
}
