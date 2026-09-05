using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SubscriptionsByUserIdSpecification : Specification<Subscription>
{
    public SubscriptionsByUserIdSpecification(string userId)
    {
        Query
            .Where(s => s.UserId == userId)
            .Include(s => s.SubscriptionPlan)
            .OrderByDescending(s => s.CreatedAt);
    }
}
