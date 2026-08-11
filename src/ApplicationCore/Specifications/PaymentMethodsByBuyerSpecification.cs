using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's saved cards, newest first.</summary>
public class PaymentMethodsByBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId)
            .OrderByDescending(pm => pm.CreatedAt);
    }
}
