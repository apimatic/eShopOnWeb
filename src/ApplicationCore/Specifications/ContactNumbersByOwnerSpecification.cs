using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of one shopper's registered numbers, newest first.</summary>
public sealed class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId)
             .OrderByDescending(c => c.RegisteredAt);
    }
}
