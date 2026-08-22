using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndCanonicalSpec : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.CanonicalNumber == canonicalNumber);
    }
}
