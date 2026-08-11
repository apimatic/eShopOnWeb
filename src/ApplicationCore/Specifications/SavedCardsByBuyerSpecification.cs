using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A buyer's saved cards, most recently added first.</summary>
public class SavedCardsByBuyerSpecification : Specification<SavedCard>
{
    public SavedCardsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedDate);
    }
}
