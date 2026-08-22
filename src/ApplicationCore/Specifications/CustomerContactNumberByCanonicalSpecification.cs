using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CustomerContactNumberByCanonicalSpecification : Specification<ContactNumber>
{
    public CustomerContactNumberByCanonicalSpecification(string buyerId, string canonicalPhoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == canonicalPhoneNumber);
    }
}
