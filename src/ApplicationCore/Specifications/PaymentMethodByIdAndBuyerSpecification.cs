using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card scoped to its owner. Scoping by <paramref name="buyerId"/> ensures one
/// shopper can never read, use or delete another's saved card.
/// </summary>
public class PaymentMethodByIdAndBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodByIdAndBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
