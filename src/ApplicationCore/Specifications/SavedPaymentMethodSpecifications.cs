using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's saved cards, newest first.</summary>
public sealed class SavedPaymentMethodsByBuyerSpecification : Specification<SavedPaymentMethod>
{
    public SavedPaymentMethodsByBuyerSpecification(string buyerId)
    {
        Query.Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt);
    }
}

/// <summary>A single saved card scoped to its owner (so one shopper cannot touch another's).</summary>
public sealed class SavedPaymentMethodByIdSpecification : Specification<SavedPaymentMethod>, ISingleResultSpecification<SavedPaymentMethod>
{
    public SavedPaymentMethodByIdSpecification(int id, string buyerId)
    {
        Query.Where(p => p.Id == id && p.BuyerId == buyerId);
    }
}
