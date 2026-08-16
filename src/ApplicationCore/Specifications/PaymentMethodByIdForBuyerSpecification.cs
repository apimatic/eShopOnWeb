using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card, scoped to its owner — so one shopper can never read, use, or delete
/// another's card. A non-matching buyer simply yields no result.
/// </summary>
public class PaymentMethodByIdForBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodByIdForBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
