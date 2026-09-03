using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperContactNumbersSpecification : Specification<ShopperContactNumber>
{
    public ShopperContactNumbersSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}

public class ShopperContactNumberByIdSpecification : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ShopperContactNumberByIdSpecification(int contactNumberId, string buyerId)
    {
        Query.Where(c => c.Id == contactNumberId && c.BuyerId == buyerId);
    }
}

public class ShopperContactNumberByCanonicalSpecification : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ShopperContactNumberByCanonicalSpecification(string buyerId, string canonicalNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.CanonicalNumber == canonicalNumber);
    }
}
