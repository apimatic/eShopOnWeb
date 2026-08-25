using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByUserSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByUserSpec(string userId)
    {
        Query.Where(pm => pm.UserId == userId);
    }
}

public class SavedPaymentMethodByIdAndUserSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdAndUserSpec(int id, string userId)
    {
        Query.Where(pm => pm.Id == id && pm.UserId == userId);
    }
}
