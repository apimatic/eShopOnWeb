using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class UserSubscriptionByMaxioIdSpecification : Specification<UserSubscription>,
    ISingleResultSpecification<UserSubscription>
{
    public UserSubscriptionByMaxioIdSpecification(long maxioSubscriptionId)
    {
        Query.Where(subscription => subscription.MaxioSubscriptionId == maxioSubscriptionId);
    }
}
