using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query
            .Where(m => m.BuyerId == buyerId)
            .OrderByDescending(m => m.CreatedAt);
    }
}
