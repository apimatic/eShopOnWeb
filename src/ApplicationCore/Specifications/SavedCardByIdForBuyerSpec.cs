using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card by its id, scoped to its owner. Ownership is enforced by the query itself:
/// another shopper's id never matches, so it cannot be seen, used, or deleted.
/// </summary>
public class SavedCardByIdForBuyerSpec : Specification<SavedCard>
{
    public SavedCardByIdForBuyerSpec(int id, string buyerId)
    {
        Query.Where(c => c.Id == id && c.BuyerId == buyerId);
    }
}
