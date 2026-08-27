using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId, bool activeOnly = true)
    {
        Query.Where(m => m.BuyerId == buyerId && (!activeOnly || m.IsActive))
            .OrderByDescending(m => m.CreatedAt);
    }
}
