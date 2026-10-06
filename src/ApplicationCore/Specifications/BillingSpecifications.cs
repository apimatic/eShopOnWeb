using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class BillingAccountByUserIdSpec : Specification<BillingAccount>
{
    public BillingAccountByUserIdSpec(string userId)
    {
        Query.Where(a => a.UserId == userId);
    }
}

public class UserSubscriptionsByUserIdSpec : Specification<UserSubscription>
{
    public UserSubscriptionsByUserIdSpec(string userId)
    {
        Query.Where(s => s.UserId == userId)
             .OrderByDescending(s => s.CreatedAtUtc);
    }
}