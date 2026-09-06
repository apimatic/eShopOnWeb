using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore;

public class MaxioCustomerByUserIdSpec : Specification<MaxioCustomer>
{
    public MaxioCustomerByUserIdSpec(string userId)
    {
        Query.Where(x => x.UserId == userId);
    }
}
