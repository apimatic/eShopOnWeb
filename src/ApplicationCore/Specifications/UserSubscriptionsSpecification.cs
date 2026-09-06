using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Ardalis.Specification;

namespace Microsoft.eShopWeb.ApplicationCore;

public class UserSubscriptionsSpecification : Specification<Subscription>
{
    public UserSubscriptionsSpecification(string userId)
    {
        Query
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt);
    }
}
