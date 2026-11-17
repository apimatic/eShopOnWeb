using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodByIdAndOwnerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndOwnerSpec(int id, string ownerId)
    {
        Query.Where(p => p.Id == id && p.OwnerId == ownerId);
    }
}
