using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single contact number, scoped to its owner so no shopper can touch another's.</summary>
public sealed class ContactNumberByIdAndOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdAndOwnerSpecification(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
