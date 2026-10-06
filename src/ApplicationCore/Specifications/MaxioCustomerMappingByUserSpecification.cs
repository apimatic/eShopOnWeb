using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioCustomerMappingByUserSpecification : Specification<MaxioCustomerMapping>
{
    public MaxioCustomerMappingByUserSpecification(string applicationUserId)
    {
        Query.Where(mapping => mapping.ApplicationUserId == applicationUserId);
    }
}
