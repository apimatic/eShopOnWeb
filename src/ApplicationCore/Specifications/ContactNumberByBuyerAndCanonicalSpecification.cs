using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumberByBuyerAndCanonicalSpecification : Specification<ShopperContactNumber>
{
    public ContactNumberByBuyerAndCanonicalSpecification(string buyerId, string canonicalNumber)
    {
        Query.Where(n => n.BuyerId == buyerId && n.CanonicalNumber == canonicalNumber);
    }
}
