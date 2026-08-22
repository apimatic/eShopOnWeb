using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByCanonicalSpecification : Specification<ShopperContactNumber>
{
    public ContactNumberByCanonicalSpecification(string canonicalNumber)
    {
        Query.Where(n => n.CanonicalNumber == canonicalNumber);
    }
}
