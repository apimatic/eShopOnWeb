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

public class SavedCardByIdSpec : Specification<SavedCard>
{
    public SavedCardByIdSpec(int savedCardId)
    {
        Query.Where(c => c.Id == savedCardId);
    }
}
