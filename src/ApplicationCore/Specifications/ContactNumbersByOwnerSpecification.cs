using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.CreatedAt);
    }
}
