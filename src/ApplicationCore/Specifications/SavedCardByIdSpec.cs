using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardByIdSpec : Specification<SavedCard>
{
    public SavedCardByIdSpec(int id)
    {
        Query.Where(c => c.Id == id);
    }
}