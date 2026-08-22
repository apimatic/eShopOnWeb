using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId, bool includeDeleted = false)
    {
        Query.Where(m => m.BuyerId == buyerId);
        if (!includeDeleted)
        {
            Query.Where(m => !m.IsDeleted);
        }
    }
}
