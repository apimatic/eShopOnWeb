using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ShopperContactNumbersByBuyerIdSpec : Specification<ShopperContactNumber>
{
    public ShopperContactNumbersByBuyerIdSpec(string buyerId)
    {
        Query.Where(number => number.BuyerId == buyerId)
            .OrderByDescending(number => number.RegisteredAt);
    }
}
