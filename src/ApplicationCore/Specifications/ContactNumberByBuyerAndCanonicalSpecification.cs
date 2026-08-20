using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndCanonicalSpecification : Specification<ContactNumber>, ISingleResultSpecification<ContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpecification(string buyerId, string canonicalNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.CanonicalNumber == canonicalNumber);
    }
}
