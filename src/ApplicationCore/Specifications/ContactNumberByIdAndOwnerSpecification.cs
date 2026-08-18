using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single contact number, scoped to its owner so one shopper can never act on another's.</summary>
public class ContactNumberByIdAndOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdAndOwnerSpecification(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
