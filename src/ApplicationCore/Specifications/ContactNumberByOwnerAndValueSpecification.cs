using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's registration of one specific canonical number — used to avoid storing duplicates.</summary>
public class ContactNumberByOwnerAndValueSpecification : Specification<ContactNumber>
{
    public ContactNumberByOwnerAndValueSpecification(string ownerId, string e164Number)
    {
        Query.Where(c => c.OwnerId == ownerId && c.E164Number == e164Number);
    }
}
