using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A single contact number, but only if it belongs to the given owner.</summary>
public class ContactNumberByIdAndOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdAndOwnerSpecification(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
