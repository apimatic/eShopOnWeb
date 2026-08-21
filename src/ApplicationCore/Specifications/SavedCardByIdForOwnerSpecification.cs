using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardByIdForOwnerSpecification : Specification<SavedCard>, ISingleResultSpecification<SavedCard>
{
    public SavedCardByIdForOwnerSpecification(int id, string ownerId)
    {
        Query.Where(c => c.Id == id && c.OwnerId == ownerId);
    }
}
