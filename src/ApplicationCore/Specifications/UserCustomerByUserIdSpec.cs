using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore;

public class UserCustomerByUserIdSpec : Specification<UserMaxioCustomer>
{
    public UserCustomerByUserIdSpec(string userId)
    {
        Query.Where(x => x.UserId == userId);
    }
}
