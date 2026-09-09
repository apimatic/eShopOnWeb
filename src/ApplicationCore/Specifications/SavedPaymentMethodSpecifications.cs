using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All cards a shopper has saved, most recent first.</summary>
public class SavedPaymentMethodsByBuyerSpec : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId)
            .OrderByDescending(m => m.CreatedAt);
    }
}

/// <summary>A single saved card by id, scoped to its owner so no shopper sees another's.</summary>
public class SavedPaymentMethodByIdSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpec(int id, string buyerId)
    {
        Query.Where(m => m.Id == id && m.BuyerId == buyerId);
    }
}

/// <summary>Most recent saved card for a shopper, used to reuse an existing PayPal customer id.</summary>
public class LatestSavedPaymentMethodByBuyerSpec : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public LatestSavedPaymentMethodByBuyerSpec(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId && m.PayPalCustomerId != null)
            .OrderByDescending(m => m.CreatedAt)
            .Take(1);
    }
}
