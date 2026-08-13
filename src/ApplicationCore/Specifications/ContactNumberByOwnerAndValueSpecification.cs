using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Finds a specific owner's registration of a given canonical number (used to avoid duplicates).</summary>
public class ContactNumberByOwnerAndValueSpecification : Specification<ContactNumber>
{
    public ContactNumberByOwnerAndValueSpecification(string ownerId, string number)
    {
        Query.Where(c => c.OwnerId == ownerId && c.Number == number);
    }
}
