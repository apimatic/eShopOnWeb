using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card, scoped to its owner. Matching on both id and buyer ensures
/// one shopper can never load, use, or delete another shopper's card.
/// </summary>
public class PaymentMethodByIdForBuyerSpec : Specification<PaymentMethod>
{
    public PaymentMethodByIdForBuyerSpec(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
