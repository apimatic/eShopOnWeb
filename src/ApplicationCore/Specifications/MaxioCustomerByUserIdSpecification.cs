using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioCustomerByUserIdSpecification : Specification<MaxioCustomerMapping>
{
    public MaxioCustomerByUserIdSpecification(string userId)
    {
        Query.Where(m => m.UserId == userId);
    }
}
