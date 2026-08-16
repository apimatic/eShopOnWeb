using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The caller's own saved cards, newest first.</summary>
public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(m => m.BuyerId == buyerId)
            .OrderByDescending(m => m.CreatedAt);
    }
}

/// <summary>A single saved card by id — ownership is enforced by the service after loading.</summary>
public class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int id)
    {
        Query.Where(m => m.Id == id);
    }
}

/// <summary>The most recent saved card for a buyer, used to reuse the PayPal customer id.</summary>
public class LatestSavedPaymentMethodByBuyerSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public LatestSavedPaymentMethodByBuyerSpecification(string buyerId)
    {
        Query
            .Where(m => m.BuyerId == buyerId)
            .OrderByDescending(m => m.CreatedAt);
    }
}
