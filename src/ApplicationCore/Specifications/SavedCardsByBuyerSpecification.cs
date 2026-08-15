using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's own saved cards, newest first.</summary>
public class SavedCardsByBuyerSpecification : Specification<SavedCard>
{
    public SavedCardsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}
