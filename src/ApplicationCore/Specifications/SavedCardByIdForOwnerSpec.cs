using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedCardByIdForOwnerSpec : Specification<SavedCard>, ISingleResultSpecification<SavedCard>
{
    public SavedCardByIdForOwnerSpec(int id, string ownerId)
    {
        Query.Where(c => c.Id == id && c.OwnerId == ownerId);
    }
}
