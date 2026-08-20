using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerByIdentitySpecification : Specification<Buyer>, ISingleResultSpecification<Buyer>
{
    public BuyerByIdentitySpecification(string identity)
    {
        Query.Where(b => b.IdentityGuid == identity)
            .Include(b => b.PaymentMethods);
    }
}
