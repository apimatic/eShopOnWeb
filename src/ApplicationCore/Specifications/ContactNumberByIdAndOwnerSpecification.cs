using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single contact number, but only when it belongs to the given owner — so one shopper can never
/// reach another's number by id.
/// </summary>
public class ContactNumberByIdAndOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdAndOwnerSpecification(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
