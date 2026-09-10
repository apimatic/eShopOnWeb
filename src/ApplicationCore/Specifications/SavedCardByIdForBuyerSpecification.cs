using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card, scoped to the owning buyer. Filtering on both id and buyer at the query
/// level guarantees one shopper can never load, use, or delete another's card.
/// </summary>
public class SavedCardByIdForBuyerSpecification : Specification<SavedCard>
{
    public SavedCardByIdForBuyerSpecification(int savedCardId, string buyerId)
    {
        Query.Where(c => c.Id == savedCardId && c.BuyerId == buyerId);
    }
}
