using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByPhoneSpecification : Specification<ContactNumber>
{
    public ContactNumberByPhoneSpecification(string canonicalPhoneNumber)
    {
        Query.Where(c => c.PhoneNumber == canonicalPhoneNumber);
    }
}
