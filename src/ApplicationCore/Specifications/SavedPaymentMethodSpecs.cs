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

public class SavedPaymentMethodByTokenSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByTokenSpec(string paymentTokenId)
    {
        Query.Where(m => m.PaymentTokenId == paymentTokenId);
    }
}

public class SavedPaymentMethodByBuyerAndDisplaySpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByBuyerAndDisplaySpec(string buyerId, string? lastDigits, string? expiry)
    {
        Query.Where(m => m.BuyerId == buyerId && m.LastDigits == lastDigits && m.Expiry == expiry);
    }
}
