using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore;

public class UserSubscriptionsByUserIdSpec : Specification<UserSubscription>
{
    public UserSubscriptionsByUserIdSpec(string userId)
    {
        Query.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt);
    }
}
