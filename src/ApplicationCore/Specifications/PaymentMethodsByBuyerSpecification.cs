using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The saved cards belonging to a shopper.</summary>
public class PaymentMethodsByBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }
}
