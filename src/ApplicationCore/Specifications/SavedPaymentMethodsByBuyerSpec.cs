using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId);
    }
}
