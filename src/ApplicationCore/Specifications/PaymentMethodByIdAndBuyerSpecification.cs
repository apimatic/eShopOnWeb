using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card scoped to its owner — so one shopper can never load, use, or delete another's.
/// </summary>
public class PaymentMethodByIdAndBuyerSpecification : Specification<PaymentMethod>
{
    public PaymentMethodByIdAndBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
