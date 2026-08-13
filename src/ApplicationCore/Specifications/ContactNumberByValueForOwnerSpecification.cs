using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A shopper's registration of a specific number, if it is still on file. Used before a resend to
/// honour the rule that nothing may be sent again to a number the shopper has removed.
/// </summary>
public class ContactNumberByValueForOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByValueForOwnerSpecification(string ownerId, string phoneNumber)
    {
        Query.Where(c => c.OwnerId == ownerId && c.PhoneNumber == phoneNumber);
    }
}
