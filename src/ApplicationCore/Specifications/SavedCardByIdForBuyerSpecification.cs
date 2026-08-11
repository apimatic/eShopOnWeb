using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card by id, scoped to its owner so one shopper can never load another's card.
/// </summary>
public class SavedCardByIdForBuyerSpecification : Specification<SavedCard>
{
    public SavedCardByIdForBuyerSpecification(int savedCardId, string buyerId)
    {
        Query.Where(c => c.Id == savedCardId && c.BuyerId == buyerId);
    }
}
