using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single contact number scoped to its owner, so one shopper can never read or delete
/// another shopper's number.
/// </summary>
public sealed class ContactNumberByIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdSpecification(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
