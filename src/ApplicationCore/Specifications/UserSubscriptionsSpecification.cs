using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionsSpecification : Specification<UserSubscription>
{
    public UserSubscriptionsSpecification(string userId)
    {
        Query.Where(us => us.UserId == userId);
    }
}
