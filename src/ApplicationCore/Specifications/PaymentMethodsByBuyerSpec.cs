using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's saved cards, newest first.</summary>
public class PaymentMethodsByBuyerSpec : Specification<PaymentMethod>
{
    public PaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(pm => pm.BuyerId == buyerId)
             .OrderByDescending(pm => pm.CreatedAt);
    }
}

/// <summary>
/// A single saved card scoped to its owner, so one shopper can never fetch, use or delete another's.
/// </summary>
public class PaymentMethodByIdForBuyerSpec : Specification<PaymentMethod>, ISingleResultSpecification<PaymentMethod>
{
    public PaymentMethodByIdForBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
