using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByOwnerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByOwnerSpec(string ownerId)
    {
        Query.Where(p => p.OwnerId == ownerId)
            .OrderByDescending(p => p.CreatedAt);
    }
}
