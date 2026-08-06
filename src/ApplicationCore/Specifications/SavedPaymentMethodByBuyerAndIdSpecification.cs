using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card, scoped to its owner. Scoping by buyer here guarantees one shopper can never
/// read, use, or delete another shopper's card.
/// </summary>
public class SavedPaymentMethodByBuyerAndIdSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByBuyerAndIdSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(pm => pm.BuyerId == buyerId && pm.Id == paymentMethodId);
    }
}
