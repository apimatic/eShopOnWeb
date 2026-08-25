using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardByBuyerIdSpec : Specification<SavedCard>
{
    public SavedCardByBuyerIdSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId && !c.IsDeleted);
    }
}
