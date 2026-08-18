using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// A single contact number, scoped to its owner so one shopper can never fetch or delete another's.
/// </summary>
public class ContactNumberByIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdSpecification(string buyerId, int contactNumberId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
