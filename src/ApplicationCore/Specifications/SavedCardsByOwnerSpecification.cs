using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByOwnerSpecification : Specification<SavedCard>
{
    public SavedCardsByOwnerSpecification(string ownerId)
    {
        Query
            .Where(c => c.OwnerId == ownerId)
            .OrderByDescending(c => c.CreatedDate);
    }
}
