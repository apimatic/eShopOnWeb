using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndCanonicalSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpecification(string buyerId, string canonicalPhoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == canonicalPhoneNumber);
    }
}
