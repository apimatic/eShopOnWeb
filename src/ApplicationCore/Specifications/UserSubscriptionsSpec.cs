using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionsSpec : Specification<Subscription>
{
    public UserSubscriptionsSpec(string userId)
    {
        Query.Where(s => s.UserId == userId);
    }
}
