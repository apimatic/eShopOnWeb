using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single saved card scoped to its owner, so one shopper can never fetch, use or delete another's.
/// </summary>
public class SavedCardByIdSpecification : Specification<SavedCard>
{
    public SavedCardByIdSpecification(int id, string buyerId)
    {
        Query.Where(c => c.Id == id && c.BuyerId == buyerId);
    }
}
