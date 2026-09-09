using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioCustomerLinkByUserIdSpec : Specification<MaxioCustomerLink>, ISingleResultSpecification<MaxioCustomerLink>
{
    public MaxioCustomerLinkByUserIdSpec(string userId)
    {
        Query.Where(link => link.UserId == userId);
    }
}

public class MaxioSubscriptionsByUserSpec : Specification<MaxioSubscriptionRecord>
{
    public MaxioSubscriptionsByUserSpec(string userId)
    {
        Query.Where(record => record.UserId == userId)
            .OrderByDescending(record => record.CreatedAt);
    }
}

public class MaxioSubscriptionsByUserAndProductSpec : Specification<MaxioSubscriptionRecord>
{
    public MaxioSubscriptionsByUserAndProductSpec(string userId, string productHandle)
    {
        Query.Where(record => record.UserId == userId && record.ProductHandle == productHandle);
    }
}
