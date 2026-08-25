using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByBuyerSpec : Specification<SavedCard>
{
    public SavedCardsByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}
