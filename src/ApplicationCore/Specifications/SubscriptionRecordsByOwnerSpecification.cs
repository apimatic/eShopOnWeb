using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class SubscriptionRecordsByOwnerSpecification : Specification<SubscriptionRecord>
{
    public SubscriptionRecordsByOwnerSpecification(string ownerId)
    {
        Query.Where(s => s.OwnerId == ownerId);
    }
}