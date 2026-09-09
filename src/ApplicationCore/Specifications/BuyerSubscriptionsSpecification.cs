using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BuyerSubscriptionsSpecification : Specification<SubscriptionRecord>
{
    public BuyerSubscriptionsSpecification(string buyerId)
    {
        Query.Where(s => s.BuyerId == buyerId);
    }

    public BuyerSubscriptionsSpecification(string buyerId, string planHandle)
    {
        Query.Where(s => s.BuyerId == buyerId && s.PlanHandle == planHandle);
    }
}
