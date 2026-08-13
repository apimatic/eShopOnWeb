using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single contact number, but only when it belongs to the given owner. Used so one shopper can never
/// see, use or delete another's number.
/// </summary>
public class ContactNumberByIdForOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForOwnerSpecification(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
