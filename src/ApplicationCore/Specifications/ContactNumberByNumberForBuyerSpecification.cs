using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's contact number matching an exact canonical number, to avoid registering duplicates.</summary>
public class ContactNumberByNumberForBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumberByNumberForBuyerSpecification(string buyerId, string canonicalNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == canonicalNumber);
    }
}
