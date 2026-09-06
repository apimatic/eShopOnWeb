using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionMappingByUserIdSpecification : Specification<UserSubscriptionMapping>
{
    public UserSubscriptionMappingByUserIdSpecification(string userId)
    {
        Query.Where(m => m.UserId == userId);
    }
}
