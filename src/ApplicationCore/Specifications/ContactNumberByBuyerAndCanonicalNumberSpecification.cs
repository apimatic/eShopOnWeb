using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndCanonicalNumberSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndCanonicalNumberSpecification(string buyerId, string canonicalPhoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == canonicalPhoneNumber);
    }
}
