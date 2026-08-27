using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByOwnerSpec : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpec(string ownerId)
    {
        Query
            .Where(c => c.OwnerId == ownerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}

public class ContactNumberByIdAndOwnerSpec : Specification<ContactNumber>
{
    public ContactNumberByIdAndOwnerSpec(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}
