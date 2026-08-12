using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single number, but only if it belongs to the given owner — so no shopper can act on another's.</summary>
public sealed class ContactNumberByIdForOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForOwnerSpecification(string ownerId, int contactNumberId)
    {
        Query.Where(c => c.OwnerId == ownerId && c.Id == contactNumberId);
    }
}
