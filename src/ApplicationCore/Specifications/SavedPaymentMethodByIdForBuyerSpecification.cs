using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card by id, but only if it belongs to the given buyer. Scoping the lookup by buyer
/// is what stops one shopper from using or deleting another's card.
/// </summary>
public class SavedPaymentMethodByIdForBuyerSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpecification(int id, string buyerId)
    {
        Query.Where(pm => pm.Id == id && pm.BuyerId == buyerId);
    }
}
