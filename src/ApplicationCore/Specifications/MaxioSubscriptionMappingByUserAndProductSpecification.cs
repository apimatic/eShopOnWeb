using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioSubscriptionMappingByUserAndProductSpecification : Specification<MaxioSubscriptionMapping>
{
    public MaxioSubscriptionMappingByUserAndProductSpecification(string applicationUserId, string productHandle)
    {
        Query.Where(mapping => mapping.ApplicationUserId == applicationUserId && mapping.ProductHandle == productHandle);
    }
}
