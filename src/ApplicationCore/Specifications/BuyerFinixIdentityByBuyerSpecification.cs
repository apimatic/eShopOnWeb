using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerFinixIdentityByBuyerSpecification : Specification<BuyerFinixIdentity>
{
    public BuyerFinixIdentityByBuyerSpecification(string buyerId)
    {
        Query.Where(i => i.BuyerId == buyerId);
    }
}
