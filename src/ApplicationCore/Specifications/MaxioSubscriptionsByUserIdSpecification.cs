using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioSubscriptionsByUserIdSpecification : Specification<MaxioSubscription>
{
    public MaxioSubscriptionsByUserIdSpecification(string userId)
    {
        Query.Where(s => s.UserId == userId)
            .OrderByDescending(s => s.SubscribedAt);
    }
}
