using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class MaxioCustomerByUserIdSpec : Specification<MaxioCustomer>
{
    public MaxioCustomerByUserIdSpec(string userId)
    {
        Query.Where(c => c.UserId == userId);
    }
}
