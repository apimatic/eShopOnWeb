using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All saved cards belonging to a given shopper.</summary>
public class PaymentMethodsByBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }
}
