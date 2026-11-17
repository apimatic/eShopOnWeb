using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SavedPaymentMethodsByBuyerSpecification : Specification<PaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId && p.Enabled)
            .OrderByDescending(p => p.CreatedAt);
    }
}
