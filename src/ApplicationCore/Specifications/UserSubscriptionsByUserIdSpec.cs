using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionsByUserIdSpec : Specification<UserSubscription>
{
    public UserSubscriptionsByUserIdSpec(string userId)
    {
        Query
            .Where(x => x.UserId == userId)
            .Include(x => x.SubscriptionPlan)
            .OrderByDescending(x => x.CreatedAt);
    }
}
