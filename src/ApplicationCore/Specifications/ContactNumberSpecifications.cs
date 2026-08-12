using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All contact numbers a given shopper has on file.</summary>
public class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId);
    }
}

/// <summary>A single contact number, scoped to its owner so no shopper can reach another's.</summary>
public class ContactNumberByIdForOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForOwnerSpecification(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
