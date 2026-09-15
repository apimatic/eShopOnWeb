using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId, bool includeDeleted = false)
    {
        Query.Where(p => p.BuyerId == buyerId);
        if (!includeDeleted)
        {
            Query.Where(p => !p.IsDeleted);
        }
    }
}

public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int id)
    {
        Query.Where(p => p.Id == id);
    }
}
