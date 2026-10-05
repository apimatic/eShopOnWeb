using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SubscriptionLinkByReferenceSpec : Specification<SubscriptionLink>
{
    public SubscriptionLinkByReferenceSpec(string subscriptionReference)
    {
        Query.Where(s => s.SubscriptionReference == subscriptionReference);
    }
}