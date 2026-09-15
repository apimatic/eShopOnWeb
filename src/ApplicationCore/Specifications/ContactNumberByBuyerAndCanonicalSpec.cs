using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndCanonicalSpec : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.CanonicalNumber == canonicalNumber);
    }
}
