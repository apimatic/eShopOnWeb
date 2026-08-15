using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's saved cards.</summary>
public class PaymentMethodsByBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId);
    }
}

/// <summary>A single saved card scoped to its owner, so a shopper only touches their own card.</summary>
public class PaymentMethodByIdAndBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodByIdAndBuyerSpecification(int id, string buyerId)
    {
        Query.Where(pm => pm.Id == id && pm.BuyerId == buyerId);
    }
}
