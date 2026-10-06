using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class MaxioSubscriptionByUserAndPlanSpec : Specification<MaxioSubscription>
{
    public MaxioSubscriptionByUserAndPlanSpec(string userId, string planHandle)
    {
        Query.Where(s => s.UserId == userId && s.PlanHandle == planHandle);
    }
}
