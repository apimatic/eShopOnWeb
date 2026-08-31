using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByOwnerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByOwnerSpecification(string ownerId)
    {
        Query.Where(m => m.OwnerId == ownerId)
            .OrderByDescending(m => m.CreatedAt);
    }
}
