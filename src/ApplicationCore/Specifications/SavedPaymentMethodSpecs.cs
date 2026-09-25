using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's saved cards.</summary>
public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId);
        Query.OrderByDescending(p => p.CreatedAt);
    }
}

/// <summary>A single saved card, scoped to its owner so one shopper cannot use another's.</summary>
public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int id, string buyerId)
    {
        Query.Where(p => p.Id == id && p.BuyerId == buyerId);
    }
}

/// <summary>The PayPal customer-id mapping for a shopper.</summary>
public class PayPalCustomerRefByBuyerSpec : Specification<PayPalCustomerRef>
{
    public PayPalCustomerRefByBuyerSpec(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}
