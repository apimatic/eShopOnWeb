using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card scoped to its owner. Filtering by buyer id (not just card id) ensures one shopper
/// can never see, use or delete another's card.
/// </summary>
public class SavedCardByIdAndBuyerSpecification : Specification<SavedCard>
{
    public SavedCardByIdAndBuyerSpecification(int paymentMethodId, string buyerId)
    {
        Query.Where(c => c.Id == paymentMethodId && c.BuyerId == buyerId);
    }
}
