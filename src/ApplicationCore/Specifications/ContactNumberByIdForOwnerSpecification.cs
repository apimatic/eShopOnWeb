using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class ContactNumberByIdForOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForOwnerSpecification(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
