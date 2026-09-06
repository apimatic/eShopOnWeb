using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionsSpecification : Specification<Subscription>
{
    public UserSubscriptionsSpecification(string userId)
    {
        Query.Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedDate);
    }
}
