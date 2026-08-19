using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByIdForOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForOwnerSpecification(string ownerId, int contactNumberId)
    {
        Query.Where(c => c.OwnerId == ownerId && c.Id == contactNumberId);
    }
}
