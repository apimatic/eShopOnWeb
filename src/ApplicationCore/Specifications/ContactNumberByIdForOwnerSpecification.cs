using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>One contact number by id, but only if it belongs to the given shopper.</summary>
public class ContactNumberByIdForOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForOwnerSpecification(string ownerId, int contactNumberId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
