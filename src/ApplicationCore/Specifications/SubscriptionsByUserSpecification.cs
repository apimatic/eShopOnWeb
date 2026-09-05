using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SubscriptionsByUserSpecification : Specification<Subscription>
{
    public SubscriptionsByUserSpecification(string userId)
    {
        Query
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt);
    }
}
