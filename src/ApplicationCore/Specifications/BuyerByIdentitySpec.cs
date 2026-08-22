using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerByIdentitySpec : Specification<Buyer>
{
    public BuyerByIdentitySpec(string identityGuid)
    {
        Query.Where(b => b.IdentityGuid == identityGuid)
            .Include(b => b.PaymentMethods);
    }
}
