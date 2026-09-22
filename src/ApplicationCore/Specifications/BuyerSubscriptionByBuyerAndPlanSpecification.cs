using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class BuyerSubscriptionByBuyerAndPlanSpecification : Specification<BuyerSubscription>
{
    public BuyerSubscriptionByBuyerAndPlanSpecification(string buyerId, string planHandle)
    {
        Query.Where(s => s.BuyerId == buyerId && s.PlanHandle == planHandle);
    }
}
