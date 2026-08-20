using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperContactNumberByCanonicalSpec : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ShopperContactNumberByCanonicalSpec(string buyerId, string phoneNumber)
    {
        Query.Where(number => number.BuyerId == buyerId && number.PhoneNumber == phoneNumber);
    }
}
