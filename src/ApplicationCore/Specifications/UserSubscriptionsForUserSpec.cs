using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All subscriptions recorded for a given eShopOnWeb user.
/// </summary>
public class UserSubscriptionsForUserSpec : Specification<UserSubscription>
{
    public UserSubscriptionsForUserSpec(string userId)
    {
        Query.Where(s => s.UserId == userId)
             .OrderByDescending(s => s.CreatedAtUtc);
    }
}
