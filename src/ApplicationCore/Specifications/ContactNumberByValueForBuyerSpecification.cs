using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's registration of a specific canonical number, used to avoid duplicate registrations.</summary>
public class ContactNumberByValueForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByValueForBuyerSpecification(string buyerId, string e164Number)
    {
        Query.Where(c => c.BuyerId == buyerId && c.E164Number == e164Number);
    }
}
