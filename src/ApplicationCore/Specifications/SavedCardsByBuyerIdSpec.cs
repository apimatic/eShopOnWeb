using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByBuyerIdSpec : Specification<SavedCard>
{
    public SavedCardsByBuyerIdSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}
