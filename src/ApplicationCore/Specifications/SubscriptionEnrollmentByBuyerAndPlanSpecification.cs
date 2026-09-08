using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SubscriptionEnrollmentByBuyerAndPlanSpecification : Specification<SubscriptionEnrollment>
{
    public SubscriptionEnrollmentByBuyerAndPlanSpecification(string buyerId, string planHandle)
    {
        Query.Where(e => e.BuyerId == buyerId && e.PlanHandle == planHandle);
    }
}
