using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Subscriptions recorded locally for a user, optionally narrowed to one plan.
/// </summary>
public class UserSubscriptionsSpecification : Specification<SubscriptionRecord>
{
    public UserSubscriptionsSpecification(string userId, string? planHandle = null)
    {
        Query.Where(r => r.UserId == userId)
             .OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrEmpty(planHandle))
        {
            Query.Where(r => r.PlanHandle == planHandle);
        }
    }
}
