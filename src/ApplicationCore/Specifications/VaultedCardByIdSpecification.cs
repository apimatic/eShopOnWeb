using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.VaultAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A saved card by its id, scoped to its owner so one shopper can never see, use, or delete another's.
/// </summary>
public class VaultedCardByIdSpecification : Specification<VaultedCard>
{
    public VaultedCardByIdSpecification(int id, string buyerId)
    {
        Query.Where(c => c.Id == id && c.BuyerId == buyerId);
    }
}
