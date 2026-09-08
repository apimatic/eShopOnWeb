using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class SubscriptionEnrollmentsByBuyerSpecification : Specification<SubscriptionEnrollment>
{
    public SubscriptionEnrollmentsByBuyerSpecification(string buyerId)
    {
        Query.Where(e => e.BuyerId == buyerId);
    }
}
