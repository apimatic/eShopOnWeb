using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore;

public class UserSubscriptionByMaxioIdSpec : Specification<UserSubscription>
{
    public UserSubscriptionByMaxioIdSpec(string userId, long maxioSubscriptionId)
    {
        Query.Where(x => x.UserId == userId && x.MaxioSubscriptionId == maxioSubscriptionId);
    }
}
