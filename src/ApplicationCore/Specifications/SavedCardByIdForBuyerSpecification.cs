using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single saved card, scoped to its owner so one shopper cannot use another's card.</summary>
public class SavedCardByIdForBuyerSpecification : Specification<SavedCard>, ISingleResultSpecification<SavedCard>
{
    public SavedCardByIdForBuyerSpecification(int savedCardId, string buyerId)
    {
        Query.Where(c => c.Id == savedCardId && c.BuyerId == buyerId);
    }
}
