using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SubscriptionRecordByReferenceSpecification : Specification<SubscriptionRecord>,
    ISingleResultSpecification<SubscriptionRecord>
{
    public SubscriptionRecordByReferenceSpecification(string subscriptionReference)
    {
        Query.Where(record => record.SubscriptionReference == subscriptionReference);
    }
}
