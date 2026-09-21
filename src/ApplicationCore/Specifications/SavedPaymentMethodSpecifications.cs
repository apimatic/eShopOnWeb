using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All cards a shopper has saved.</summary>
public class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(m => m.BuyerId == buyerId);
    }
}

/// <summary>A single saved card, scoped to its owner so one shopper can never act on another's.</summary>
public class SavedPaymentMethodByIdForBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdForBuyerSpecification(int id, string buyerId)
    {
        Query.Where(m => m.Id == id && m.BuyerId == buyerId);
    }
}
