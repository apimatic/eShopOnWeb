using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByBuyerSpecification : Specification<SavedCard>
{
    public SavedCardsByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}

public class SavedCardByIdSpecification : Specification<SavedCard>
{
    public SavedCardByIdSpecification(int savedCardId)
    {
        Query.Where(c => c.Id == savedCardId);
    }
}
