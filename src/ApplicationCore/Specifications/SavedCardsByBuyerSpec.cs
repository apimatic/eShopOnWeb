using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SavedCardsByBuyerSpec : Specification<SavedCard>
{
    public SavedCardsByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId && !c.IsDeleted);
    }
}
