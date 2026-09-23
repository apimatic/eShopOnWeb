using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A buyer's saved cards, newest first.</summary>
public class SavedCardsByBuyerSpec : Specification<SavedCard>
{
    public SavedCardsByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}

/// <summary>A single saved card by id, scoped to its owner (enforces ownership).</summary>
public class SavedCardByIdForBuyerSpec : Specification<SavedCard>, ISingleResultSpecification<SavedCard>
{
    public SavedCardByIdForBuyerSpec(int id, string buyerId)
    {
        Query.Where(c => c.Id == id && c.BuyerId == buyerId);
    }
}
