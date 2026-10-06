using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionsSpecification : Specification<UserSubscription>
{
    public UserSubscriptionsSpecification(string userId)
    {
        Query.Where(subscription => subscription.UserId == userId);
    }
}

public class UserSubscriptionForPlanSpecification : Specification<UserSubscription>
{
    public UserSubscriptionForPlanSpecification(string userId, string planHandle)
    {
        Query.Where(subscription => subscription.UserId == userId && subscription.PlanHandle == planHandle);
    }
}
