using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single saved card, scoped to its owner so one shopper can never reach another's card.</summary>
public class SavedCardByBuyerAndIdSpecification : Specification<SavedCard>
{
    public SavedCardByBuyerAndIdSpecification(string buyerId, int paymentMethodId)
    {
        Query.Where(c => c.BuyerId == buyerId && c.Id == paymentMethodId);
    }
}
