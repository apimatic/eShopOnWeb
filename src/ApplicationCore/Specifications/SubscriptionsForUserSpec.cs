using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SubscriptionsForUserSpec : Specification<SubscriptionRecord>
{
    public SubscriptionsForUserSpec(string userId)
    {
        Query.Where(s => s.UserId == userId)
            .OrderByDescending(s => s.Id);
    }
}
