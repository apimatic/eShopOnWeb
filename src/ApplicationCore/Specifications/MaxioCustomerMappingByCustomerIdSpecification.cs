using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioCustomerMappingByCustomerIdSpecification : Specification<MaxioCustomerMapping>
{
    public MaxioCustomerMappingByCustomerIdSpecification(int maxioCustomerId)
    {
        Query.Where(m => m.MaxioCustomerId == maxioCustomerId);
    }
}
