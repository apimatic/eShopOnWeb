using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

    public class ShopperContactNumberByCanonicalSpecification : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ShopperContactNumberByCanonicalSpecification(string buyerId, string canonicalPhoneNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == canonicalPhoneNumber);
    }
}
