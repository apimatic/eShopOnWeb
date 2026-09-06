using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionsSpecification : Specification<Subscription>
{
    public UserSubscriptionsSpecification(string identityId)
    {
        Query
            .Where(s => s.IdentityId == identityId)
            .OrderByDescending(s => s.CreatedAt);
    }
}
