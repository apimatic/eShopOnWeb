using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's saved cards, most recently added first.</summary>
public class SavedCardsByBuyerSpecification : Specification<SavedCard>
{
    public SavedCardsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}

/// <summary>A single saved card, scoped to its owner so one shopper can never load another's.</summary>
public class SavedCardByIdForBuyerSpecification : Specification<SavedCard>
{
    public SavedCardByIdForBuyerSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(c => c.Id == paymentMethodId && c.BuyerId == buyerId);
    }
}
