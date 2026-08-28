using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A saved card, scoped to its owner. Scoping in the query — rather than loading by id and checking
/// afterwards — is what makes it impossible to read or delete another shopper's card by accident.
/// </summary>
public class SavedCardByIdForBuyerSpecification : Specification<SavedCard>, ISingleResultSpecification<SavedCard>
{
    public SavedCardByIdForBuyerSpecification(int id, string buyerId)
    {
        Query.Where(c => c.Id == id && c.BuyerId == buyerId);
    }
}
