using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A saved card by its id, scoped to its owner — so one shopper can never resolve another's card.</summary>
public class SavedCardByIdSpec : Specification<SavedCard>
{
    public SavedCardByIdSpec(string buyerId, int paymentMethodId)
    {
        Query.Where(c => c.Id == paymentMethodId && c.BuyerId == buyerId);
    }
}
