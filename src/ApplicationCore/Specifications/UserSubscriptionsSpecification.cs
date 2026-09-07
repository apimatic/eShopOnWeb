using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class UserSubscriptionsSpecification : Specification<MaxioSubscription>
{
    public UserSubscriptionsSpecification(string userId)
    {
        Query.Where(s => s.UserId == userId);
    }
}
