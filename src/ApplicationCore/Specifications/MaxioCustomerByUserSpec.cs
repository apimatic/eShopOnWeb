using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class MaxioCustomerByUserSpec : Specification<MaxioCustomer>
{
    public MaxioCustomerByUserSpec(string userId)
    {
        Query.Where(c => c.UserId == userId);
    }
}
