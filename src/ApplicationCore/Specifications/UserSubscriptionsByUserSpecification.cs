using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionsByUserSpecification : Specification<UserSubscription>
{
    public UserSubscriptionsByUserSpecification(string userId)
    {
        Query.Where(s => s.UserId == userId);
    }
}
