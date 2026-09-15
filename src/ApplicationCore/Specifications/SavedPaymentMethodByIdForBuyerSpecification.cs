using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card by id, scoped to its owner — so one shopper can never read, use or
/// delete another's card (an id owned by someone else simply returns nothing).
/// </summary>
public class SavedPaymentMethodByIdForBuyerSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(pm => pm.Id == paymentMethodId && pm.BuyerId == buyerId);
    }
}
