using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every contact number a shopper has on file.</summary>
public class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.RegisteredAt);
    }
}

/// <summary>A single contact number, scoped to its owner so no shopper can touch another's.</summary>
public class ContactNumberByOwnerAndIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByOwnerAndIdSpecification(string ownerId, int contactNumberId)
    {
        Query.Where(c => c.OwnerId == ownerId && c.Id == contactNumberId);
    }
}

/// <summary>Detects a shopper re-registering a number they already have on file.</summary>
public class ContactNumberByOwnerAndNumberSpecification : Specification<ContactNumber>
{
    public ContactNumberByOwnerAndNumberSpecification(string ownerId, string phoneNumber)
    {
        Query.Where(c => c.OwnerId == ownerId && c.PhoneNumber == phoneNumber);
    }
}
