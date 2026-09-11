using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class UserSubscriptionsByUserIdSpec : Specification<UserSubscription>
{
    public UserSubscriptionsByUserIdSpec(string userId)
    {
        Query.Where(s => s.UserId == userId)
             .OrderByDescending(s => s.CreatedAt);
    }
}
