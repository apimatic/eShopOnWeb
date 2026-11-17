using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CustomerPaymentProfileByOwnerSpec : Specification<CustomerPaymentProfile>
{
    public CustomerPaymentProfileByOwnerSpec(string ownerId)
    {
        Query.Where(p => p.OwnerId == ownerId);
    }
}
