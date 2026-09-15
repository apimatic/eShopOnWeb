using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card, but only if it belongs to the given buyer — so one shopper can never load,
/// use or delete another's card.
/// </summary>
public class SavedCardByIdForBuyerSpecification : Specification<SavedCard>
{
    public SavedCardByIdForBuyerSpecification(int id, string buyerId)
    {
        Query.Where(c => c.Id == id && c.BuyerId == buyerId);
    }
}
