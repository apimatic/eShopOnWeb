using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioCustomerMappingByUserNameSpecification : Specification<MaxioCustomerMapping>
{
    public MaxioCustomerMappingByUserNameSpecification(string userName)
    {
        Query.Where(m => m.UserName == userName);
    }
}
