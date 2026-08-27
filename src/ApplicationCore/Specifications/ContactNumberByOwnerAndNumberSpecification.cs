using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByOwnerAndNumberSpecification : Specification<ContactNumber>
{
    public ContactNumberByOwnerAndNumberSpecification(string ownerId, string phoneNumber)
    {
        Query.Where(c => c.OwnerId == ownerId && c.PhoneNumber == phoneNumber);
    }
}
