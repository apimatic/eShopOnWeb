using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All contact numbers registered by one shopper.</summary>
public sealed class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId);
    }
}

/// <summary>A single contact number by id, scoped to its owner (so one shopper can never touch another's).</summary>
public sealed class ContactNumberByOwnerAndIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByOwnerAndIdSpecification(string ownerId, int contactNumberId)
    {
        Query.Where(c => c.OwnerId == ownerId && c.Id == contactNumberId);
    }
}
