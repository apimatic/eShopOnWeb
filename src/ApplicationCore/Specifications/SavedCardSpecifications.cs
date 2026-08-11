using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a buyer's saved cards.</summary>
public class SavedCardsByBuyerSpecification : Specification<SavedCard>
{
    public SavedCardsByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}

/// <summary>A single saved card, scoped to its owner so no shopper can reach another's card.</summary>
public class SavedCardByIdSpecification : Specification<SavedCard>
{
    public SavedCardByIdSpecification(int savedCardId, string buyerId)
    {
        Query.Where(c => c.Id == savedCardId && c.BuyerId == buyerId);
    }
}
