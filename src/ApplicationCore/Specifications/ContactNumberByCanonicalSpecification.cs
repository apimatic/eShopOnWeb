using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByCanonicalSpecification : Specification<ContactNumber>
{
    public ContactNumberByCanonicalSpecification(string canonicalNumber)
    {
        Query.Where(c => c.CanonicalNumber == canonicalNumber);
    }
}
