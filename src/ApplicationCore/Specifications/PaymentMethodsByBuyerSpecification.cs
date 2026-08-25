using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class PaymentMethodsByBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt);
    }
}
