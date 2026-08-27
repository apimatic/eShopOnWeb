using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId);
    }
}

public class ContactNumberByIdAndOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdAndOwnerSpecification(int contactNumberId, string ownerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.OwnerId == ownerId);
    }
}

public class ContactNumberByOwnerAndNumberSpecification : Specification<ContactNumber>
{
    public ContactNumberByOwnerAndNumberSpecification(string ownerId, string phoneNumber)
    {
        Query.Where(c => c.OwnerId == ownerId && c.PhoneNumber == phoneNumber);
    }
}
