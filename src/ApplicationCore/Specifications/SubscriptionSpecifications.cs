using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Specifications;

public class SubscriptionsByUserIdSpecification : Specification<Subscription>
{
    public SubscriptionsByUserIdSpecification(string userId)
    {
        Query.Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedUtc);
    }
}

public class SubscriptionByMaxioIdSpecification : Specification<Subscription>
{
    public SubscriptionByMaxioIdSpecification(long maxioSubscriptionId)
    {
        Query.Where(s => s.MaxioSubscriptionId == maxioSubscriptionId);
    }
}
