using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByShopperSpec : Specification<SavedCard>
{
    public SavedCardsByShopperSpec(string shopperId)
    {
        Query.Where(c => c.ShopperId == shopperId && !c.IsDeleted);
    }
}
