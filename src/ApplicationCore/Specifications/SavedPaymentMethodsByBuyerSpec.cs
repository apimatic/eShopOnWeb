using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query
            .Where(m => m.BuyerId == buyerId)
            .OrderBy(m => m.Id);
    }
}
