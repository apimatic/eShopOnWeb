using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardsByOwnerSpec : Specification<SavedCard>
{
    public SavedCardsByOwnerSpec(string ownerId)
    {
        Query
            .Where(c => c.OwnerId == ownerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}
