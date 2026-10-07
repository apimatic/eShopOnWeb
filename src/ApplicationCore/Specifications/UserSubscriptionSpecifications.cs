using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionsForUserSpec : Specification<UserSubscription>
{
    public UserSubscriptionsForUserSpec(string userId)
    {
        Query.Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt);
    }
}

public class UserSubscriptionForUserAndPlanSpec : Specification<UserSubscription>
{
    public UserSubscriptionForUserAndPlanSpec(string userId, string planHandle)
    {
        Query.Where(s => s.UserId == userId && s.PlanHandle == planHandle);
    }
}
