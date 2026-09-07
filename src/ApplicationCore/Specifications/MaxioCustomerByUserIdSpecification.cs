using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class MaxioCustomerByUserIdSpecification : Specification<MaxioCustomer>
{
    public MaxioCustomerByUserIdSpecification(string userId)
    {
        Query.Where(c => c.UserId == userId);
    }
}
