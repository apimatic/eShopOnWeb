using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioSubscriptionForUserPlanSpecification : Specification<MaxioSubscription>
{
    public MaxioSubscriptionForUserPlanSpecification(string userId, string planHandle)
    {
        Query.Where(subscription => subscription.UserId == userId && subscription.PlanHandle == planHandle);
    }
}