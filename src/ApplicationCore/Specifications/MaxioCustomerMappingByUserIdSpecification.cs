using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioCustomerMappingByUserIdSpecification : Specification<MaxioCustomerMapping>
{
    public MaxioCustomerMappingByUserIdSpecification(string applicationUserId)
    {
        Query.Where(m => m.ApplicationUserId == applicationUserId);
    }
}
