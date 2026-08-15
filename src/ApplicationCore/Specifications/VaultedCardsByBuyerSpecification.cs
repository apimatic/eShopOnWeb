using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.VaultAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class VaultedCardsByBuyerSpecification : Specification<VaultedCard>
{
    public VaultedCardsByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId);
    }
}
