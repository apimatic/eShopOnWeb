using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Scopes a contact number to its owner so one shopper can never fetch another's.</summary>
public class ContactNumberByIdForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForBuyerSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}
