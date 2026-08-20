using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperContactNumberByIdSpec : Specification<ShopperContactNumber>, ISingleResultSpecification<ShopperContactNumber>
{
    public ShopperContactNumberByIdSpec(int contactNumberId, string buyerId)
    {
        Query.Where(n => n.Id == contactNumberId && n.BuyerId == buyerId);
    }
}
