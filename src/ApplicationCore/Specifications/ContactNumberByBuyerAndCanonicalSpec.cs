using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndCanonicalSpec : Specification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.CanonicalNumber == canonicalNumber);
    }
}
