using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SavedCardByIdSpec : Specification<SavedCard>
{
    public SavedCardByIdSpec(int id, string buyerId)
    {
        Query.Where(c => c.Id == id && c.BuyerId == buyerId && !c.IsDeleted);
    }
}
