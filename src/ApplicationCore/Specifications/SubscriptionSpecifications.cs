using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SubscriptionsForUserSpecification : Specification<Subscription>
{
    public SubscriptionsForUserSpecification(string userId)
    {
        Query
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedUtc);
    }
}

public sealed class SubscriptionForUserAndPlanSpecification : Specification<Subscription>
{
    public SubscriptionForUserAndPlanSpecification(string userId, string planHandle)
    {
        Query
            .Where(s => s.UserId == userId && s.PlanHandle == planHandle);
    }
}