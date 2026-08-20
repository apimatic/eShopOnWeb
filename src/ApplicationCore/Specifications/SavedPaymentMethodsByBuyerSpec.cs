using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId)
            .OrderByDescending(m => m.CreatedAt);
    }
}

public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int paymentMethodId, string? buyerId = null)
    {
        Query.Where(m => m.Id == paymentMethodId);
        if (!string.IsNullOrEmpty(buyerId))
        {
            Query.Where(m => m.BuyerId == buyerId);
        }
    }
}
