using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByBuyerIdSpecification : Specification<SavedCard>
{
    public SavedCardsByBuyerIdSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderBy(c => c.Id);
    }
}

public class SavedCardByIdSpecification : Specification<SavedCard>
{
    public SavedCardByIdSpecification(int savedCardId)
    {
        Query.Where(c => c.Id == savedCardId);
    }
}
