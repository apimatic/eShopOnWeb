using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A saved card by id, but only if it belongs to the given shopper.</summary>
public class SavedCardByIdForBuyerSpecification : Specification<SavedCard>
{
    public SavedCardByIdForBuyerSpecification(int savedCardId, string buyerId)
    {
        Query.Where(c => c.Id == savedCardId && c.BuyerId == buyerId);
    }
}
