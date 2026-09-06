using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore;

public class MaxioCustomerMappingByUserIdSpec : Specification<MaxioCustomerMapping>
{
    public MaxioCustomerMappingByUserIdSpec(string userId)
    {
        Query.Where(m => m.UserId == userId);
    }
}
