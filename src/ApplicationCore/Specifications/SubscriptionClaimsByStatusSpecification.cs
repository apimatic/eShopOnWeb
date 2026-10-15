using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SubscriptionClaimsByStatusSpecification : Specification<SubscriptionClaim>
{
    public SubscriptionClaimsByStatusSpecification(string userKey, BillingClaimStatus status)
    {
        Query.Where(c => c.UserKey == userKey && c.Status == status);
    }
}
