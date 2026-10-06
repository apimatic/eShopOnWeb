using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SubscriptionRecordsByOwnerAndPlanSpecification : Specification<SubscriptionRecord>
{
    public SubscriptionRecordsByOwnerAndPlanSpecification(string ownerId, string planHandle)
    {
        Query.Where(s => s.OwnerId == ownerId && s.PlanHandle == planHandle);
    }
}